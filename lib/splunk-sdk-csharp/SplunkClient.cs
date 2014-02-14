﻿/*
 * Copyright 2014 Splunk, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"): you may
 * not use this file except in compliance with the License. You may obtain
 * a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

namespace Splunk.Sdk
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Runtime;
    using System.Threading.Tasks;
    using System.Xml.Linq;

    /// <summary>
    /// Provides a class for sending HTTP requests and receiving HTTP responses from a Splunk server.
    /// </summary>
    public sealed class SplunkClient
    {
        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="SplunkClient"/> class with a protocol, host, and port number.
        /// </summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to communiate with <see cref="Host"/></param>
        /// <param name="host">The DNS name of a Splunk server instance</param>
        /// <param name="port">The port number used to communicate with <see cref="Host"/></param>
        public SplunkClient(Protocol protocol, string host, int port)
        {
            this.Initialize(protocol, host, port, null, false);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SplunkClient"/> class with a protocol, host, and port number.
        /// </summary>
        /// <param name="protocol">The <see cref="Protocol"/> used to communiate with <see cref="Host"/></param>
        /// <param name="host">The DNS name of a Splunk server instance</param>
        /// <param name="port">The port number used to communicate with <see cref="Host"/></param>
        /// <param name="handler"></param>
        /// <param name="disposeHandler"></param>
        public SplunkClient(Protocol protocol, string host, int port, HttpMessageHandler handler, bool disposeHandler = true)
        {
            Contract.Requires<ArgumentNullException>(handler != null);
            Initialize(protocol, host, port, handler, disposeHandler);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the host associated with this instance.
        /// </summary>
        public string Host
        { get; private set; }

        /// <summary>
        /// Gets the management port number used to communicate with the <see cref="Host"/> associated with this instance.
        /// </summary>
        public int Port
        { get; private set; }

        /// <summary>
        /// Gets the protocol used to communicate with the <see cref="Host"/> associated with this instance.
        /// </summary>
        public Protocol Protocol 
        { get; private set; }

        /// <summary>
        /// Gets the session key associated with this instance.
        /// </summary>
        /// <remarks>
        /// The value returned is null until <see cref="Login"/> is successfully completed.
        /// </remarks>
        public string SessionKey
        { get; private set; }

        #endregion

        #region Methods

        /// <summary>
        /// Provides user authentication.
        /// </summary>
        /// <param name="username">The Splunk account username.</param>
        /// <param name="password">The password for the user specified with username.</param>
        /// <remarks>This method uses the Splunk <a href="http://goo.gl/yXJX75">auth/login</a> 
        /// endpoint. The session key that it returns is used for subsequent requests. It is
        /// accessible via the <see cref="SessionKey"/> property.
        /// </remarks>
        public async Task Login(string username, string password)
        {
            Contract.Requires(username != null);
            Contract.Requires(password != null);

            using (var content = new StringContent(string.Format("username={0}\npassword={1}", username, password)))
            {
                HttpResponseMessage message = await this.client.PostAsync(this.CreateUri(new string[] { "auth", "login" }), content);
                string messageBody = await message.Content.ReadAsStringAsync();
                
                if (!message.IsSuccessStatusCode)
                {
                    throw new SplunkRequestException(message.StatusCode, message.ReasonPhrase, details: messageBody);
                }

                this.SessionKey = XDocument.Parse(messageBody).Element("sessionKey").Value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="namespace"></param>
        /// <param name="resource"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public async Task<XDocument> GetDocument(Namespace @namespace, string[] resource, Dictionary<string, object> parameters)
        {
            string messageBody = await this.client.GetStringAsync(this.CreateUri(@namespace, resource, parameters));
            return XDocument.Parse(messageBody);
        }

        public async Task<XDocument> GetDocument(string[] resource, Dictionary<string, object> parameters)
        {
            string messageBody = await this.client.GetStringAsync(this.CreateUri(resource, parameters));
            return XDocument.Parse(messageBody);
        }

        public async Task<Stream> GetDocumentStream(Namespace @namespace, string[] resource, Dictionary<string, object> parameters)
        {
            Stream stream = await this.client.GetStreamAsync(this.CreateUri(@namespace, resource, parameters));
            return stream;
        }

        public async Task<Stream> GetDocumentStream(string[] resource, Dictionary<string, object> parameters)
        {
            Stream stream = await this.client.GetStreamAsync(this.CreateUri(resource, parameters));
            return stream;
        }

        public XDocument Post(Namespace @namespace, params string[] resource)
        {
            throw new NotImplementedException();
        }

        public XDocument Post(params string[] resource)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return string.Concat(Scheme[(int)this.Protocol], "://", this.Host, ":", this.Port.ToString());
        }

        #endregion

        #region Privates

        private static readonly string[] Scheme = { "http", "https" };
        private HttpClient client;

        private Uri CreateUri(Namespace @namespace, string[] resource, Dictionary<string, object> parameters = null)
        {
            return this.CreateUri(new string[] { string.Empty, "servicesNS", @namespace.User, @namespace.App }.Concat(resource), parameters);
        }

        private Uri CreateUri(string[] resource, Dictionary<string, object> parameters = null)
        {
            return this.CreateUri(new string[] { string.Empty, "services" }.Concat(resource), parameters);
        }

        private Uri CreateUri(IEnumerable<string> segments, Dictionary<string, object> parameters)
        {
            var builder = new UriBuilder(Scheme[(int)this.Protocol], this.Host, this.Port);

            builder.Path = string.Join("/", segments.Select(segment => Uri.EscapeUriString(segment)));

            if (parameters != null)
            {
                builder.Query = string.Join("&", parameters.Select(parameter => string.Join("=", Uri.EscapeUriString(parameter.Key), Uri.EscapeUriString(parameter.Value.ToString()))));
            }

            return builder.Uri;
        }

        private void Initialize(Protocol protocol, string host, int port, HttpMessageHandler handler, bool disposeHandler)
        {
            this.Protocol = protocol;
            this.Host = host;
            this.Port = port;
            this.client = this.client == null ? new HttpClient() : new HttpClient(handler, disposeHandler);
        }

        #endregion
    }
}