﻿using EveOpenApi.Api;
using EveOpenApi.Authentication;
using EveOpenApi.Interfaces;
using Microsoft.OpenApi.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
[assembly: InternalsVisibleTo("Eve-OpenApi.Test")]

namespace EveOpenApi.Managers
{
	internal class RequestManager : BaseManager, IRequestManager
	{
		ICacheManager cacheManager;
		IFactory<IApiRequest> apiRequestFactory;

		OpenApiDocument spec;

		public RequestManager(IHttpHandler client, IApiConfig config, ILogin login, ICacheManager cacheManager, IFactory<IApiRequest> apiRequestFactory, OpenApiDocument spec) : base(client, login, config)
		{
			this.apiRequestFactory = apiRequestFactory;
			this.cacheManager = cacheManager;
			this.spec = spec;
		}

		/// <summary>
		/// Request multiple queries for the same path.
		/// </summary>
		/// <param name="path">Esi path</param>
		/// <param name="user">User preforming this query.</param>
		/// <param name="type">Operation Type.</param>
		/// <param name="parameters">Parameters supplide by the user.</param>
		/// <param name="operation">OpenAPI operation for this path.</param>
		/// <returns></returns>
		public async Task<IEnumerable<IApiResponse>> RequestBatch(string path, OperationType type, Dictionary<string, List<object>> parameters, List<string> users, OpenApiOperation operation)
		{
			IEnumerable<IApiRequest> requests = GetRequest(path, type, parameters, users, operation);
			return await cacheManager.GetResponse(requests);
		}

		/// <summary>
		/// Request multiple queries for the same path.
		/// </summary>
		/// <typeparam name="T">Esi response return type.</typeparam>
		/// <param name="path">Esi path</param>
		/// <param name="user">User preforming this query.</param>
		/// <param name="type">Operation Type.</param>
		/// <param name="parameters">Parameters supplide by the user.</param>
		/// <param name="operation">OpenAPI operation for this path.</param>
		/// <returns></returns>
		public async Task<IEnumerable<IApiResponse<T>>> RequestBatch<T>(string path, OperationType type, Dictionary<string, List<object>> parameters, List<string> users, OpenApiOperation operation)
		{
			IEnumerable<IApiRequest> requests = GetRequest(path, type, parameters, users, operation);
			return await cacheManager.GetResponse<T>(requests);
		}

		public IEnumerable<IApiRequest> GetRequest(string path, OperationType type, Dictionary<string, List<object>> parameters, List<string> users, OpenApiOperation operation)
		{
			ParsedParameters parsed = ParseParameters(operation, parameters, users);
			string baseUrl = $"{spec.Servers[0].Url}";
			string scope = GetScope(operation);
			HttpMethod httpMethod = OperationToMethod(type);

			IApiRequest[] requests = new IApiRequest[parsed.MaxLength];
			for (int i = 0; i < parsed.MaxLength; i++)
			{
				string url = GetRequestUrl($"{baseUrl}{path}?", parsed, i);
				IDictionary<string, string> headers = parsed.Headers.ToDictionary(x => x.Key, x => FirstOrIndex(x.Value, i));

				requests[i] = apiRequestFactory.Create(new Uri(url), FirstOrIndex(users, i), scope, headers, httpMethod);
			}

			return requests;
		}

		/// <summary>
		/// Sort parameters into their respective group.
		/// </summary>
		/// <param name="operation"></param>
		/// <param name="parameters"></param>
		/// <returns></returns>
		ParsedParameters ParseParameters(OpenApiOperation operation, Dictionary<string, List<object>> parameters, List<string> users)
		{
			int maxLength = 1;
			var queries = new List<KeyValuePair<string, List<string>>>();
			var headers = new List<KeyValuePair<string, List<string>>>();
			var pathParameters = new List<KeyValuePair<string, List<string>>>();

			foreach (var item in operation.Parameters)
			{
				if (parameters.TryGetValue(item.Name, out List<object> value))
				{
					// Verify all all requests in a batch request has a parameter
					if (maxLength == 1 && value.Count > maxLength)
						maxLength = value.Count;
					else if (maxLength > 1 && value.Count != maxLength)
						throw new Exception("Every batch request must have one parameter for all or one parameter for each.");

					var kvp = new KeyValuePair<string, List<string>>(item.Name, value.Select(a => a.ToString()).ToList());
					switch (item.In)
					{
						case ParameterLocation.Query:
							queries.Add(kvp);
							break;
						case ParameterLocation.Path:
							pathParameters.Add(kvp);
							break;
						case ParameterLocation.Header:
							headers.Add(kvp);
							break;
						default:
							break;
					}
				}
				else if (item.Required && Config.TokenLocation != "query" && Config.TokenName == item.Name)
					throw new Exception($"Required parameter '{item.Name}' not supplied.");
			}

			if (users.Count != 1 && users.Count != maxLength)
				throw new Exception("Number of users must be 1 or same count as batch parameters");

			return new ParsedParameters(maxLength, queries, headers, pathParameters, users);
		}

		/// <summary>
		/// Try get scope from operation.
		/// </summary>
		/// <param name="operation"></param>
		/// <returns></returns>
		string GetScope(OpenApiOperation operation)
		{
			List<string> scopes = operation.Security?.FirstOrDefault()?.FirstOrDefault().Value as List<string>;

			if (scopes != null && scopes.Count > 0)
				return scopes[0];

			return "";
		}

		/// <summary>
		/// Convert OperationType to HttpMethod
		/// </summary>
		/// <param name="operation"></param>
		/// <returns></returns>
		HttpMethod OperationToMethod(OperationType operation)
		{
			switch (operation)
			{
				case OperationType.Get:
					return HttpMethod.Get;
				case OperationType.Put:
					return HttpMethod.Put;
				case OperationType.Post:
					return HttpMethod.Post;
				case OperationType.Delete:
					return HttpMethod.Delete;
				case OperationType.Options:
					return HttpMethod.Options;
				case OperationType.Head:
					return HttpMethod.Head;
				case OperationType.Patch:
					return HttpMethod.Patch;
				case OperationType.Trace:
					return HttpMethod.Trace;
				default:
					throw new Exception("Dafuq");
			}
		}

		/// <summary>
		/// Create a request url from parsed parameters
		/// </summary>
		/// <param name="index"></param>
		/// <returns></returns>
		string GetRequestUrl(string basePath, ParsedParameters parsed, int index)
		{
			// Replace paremetrs in path with correct value
			foreach (var item in parsed.PathParameters)
				basePath = basePath.Replace($"{{{item.Key}}}", $"{FirstOrIndex(item.Value, index)}");

			foreach (var item in parsed.Queries)
				basePath += $"{item.Key}={FirstOrIndex(item.Value, index)}&";

			return basePath[0..^1]; // Removes last &
		}

		/// <summary>
		/// If the list only has one item always use the first item.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="list"></param>
		/// <param name="index"></param>
		/// <returns></returns>
		T FirstOrIndex<T>(List<T> list, int index)
		{
			if (list.Count == 1)
				return list[0];

			return list[index];
		}
	}
}
