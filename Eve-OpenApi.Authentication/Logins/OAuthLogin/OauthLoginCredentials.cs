﻿using System;
using System.Collections.Generic;
using System.Text;

namespace EveOpenApi.Authentication
{
	public class OauthLoginCredentials : ILoginCredentials
	{
		public string ClientID { get; set; }

		public string ClientSecret { get; set; }

		public string Callback { get; set; }
	}
}
