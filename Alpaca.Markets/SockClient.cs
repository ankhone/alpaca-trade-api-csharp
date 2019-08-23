﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Alpaca.Markets
{
    /// <summary>
    /// Provides unified type-safe access for Alpaca streaming API.
    /// </summary>
    [SuppressMessage(
        "Globalization","CA1303:Do not pass literals as localized parameters",
        Justification = "We do not plan to support localized exception messages in this SDK.")]
    // ReSharper disable once PartialTypeWithSinglePart
    public sealed partial class SockClient : SockClientBase
    {
        private readonly String _keyId;

        private readonly String _secretKey;

        /// <summary>
        /// Creates new instance of <see cref="SockClient"/> object.
        /// </summary>
        /// <param name="keyId">Application key identifier.</param>
        /// <param name="secretKey">Application secret key.</param>
        /// <param name="alpacaRestApi">Alpaca REST API endpoint URL.</param>
        /// <param name="webSocketFactory">Factory class for web socket wrapper creation.</param>
        public SockClient(
            String keyId,
            String secretKey,
            String alpacaRestApi = null,
            IWebSocketFactory webSocketFactory = null)
            : this(
                keyId,
                secretKey,
                new Uri(alpacaRestApi ?? "https://api.alpaca.markets"),
                webSocketFactory ?? new WebSocket4NetFactory())
        {
        }

        /// <summary>
        /// Creates new instance of <see cref="SockClient"/> object.
        /// </summary>
        /// <param name="keyId">Application key identifier.</param>
        /// <param name="secretKey">Application secret key.</param>
        /// <param name="alpacaRestApi">Alpaca REST API endpoint URL.</param>
        /// <param name="webSocketFactory">Factory class for web socket wrapper creation.</param>
        public SockClient(
            String keyId,
            String secretKey,
            Uri alpacaRestApi,
            IWebSocketFactory webSocketFactory)
            : base(getUriBuilder(alpacaRestApi), webSocketFactory)
        {
            _keyId = keyId ?? throw new ArgumentException(
                         "Application key id should not be null", nameof(keyId));
            _secretKey = secretKey ?? throw new ArgumentException(
                             "Application secret key should not be null", nameof(secretKey));
        }

        /// <summary>
        /// Occured when new account update received from stream.
        /// </summary>
        public event Action<IAccountUpdate> OnAccountUpdate;

        /// <summary>
        /// Occured when new trade update received from stream.
        /// </summary>
        public event Action<ITradeUpdate> OnTradeUpdate;

        /// <inheritdoc/>
        protected override void OnOpened()
        {
            SendAsJsonString(new JsonAuthRequest
            {
                Action = JsonAction.Authenticate,
                Data = new JsonAuthRequest.JsonData()
                {
                    KeyId = _keyId,
                    SecretKey = _secretKey
                }
            });

            base.OnOpened();
        }

        /// <inheritdoc/>
        [SuppressMessage(
            "Design", "CA1031:Do not catch general exception types",
            Justification = "Expected behavior - we report exceptions via OnError event.")]
        protected override void OnDataReceived(
            Byte[] binaryData)
        {
            try
            {
                var message = Encoding.UTF8.GetString(binaryData);
                var root = JObject.Parse(message);

                var data = root["data"];
                var stream = root["stream"].ToString();

                switch (stream)
                {
                    case "authorization":
                        handleAuthorization(
                            data.ToObject<JsonAuthResponse>());
                        break;

                    case "listening":
                        OnConnected(AuthStatus.Authorized);
                        break;

                    case "trade_updates":
                        handleTradeUpdates(
                            data.ToObject<JsonTradeUpdate>());
                        break;

                    case "account_updates":
                        handleAccountUpdates(
                            data.ToObject<JsonAccountUpdate>());
                        break;

                    default:
                        HandleError(new InvalidOperationException(
                            $"Unexpected message type '{stream}' received."));
                        break;
                }
            }
            catch (Exception exception)
            {
                HandleError(exception);
            }
        }

        private static UriBuilder getUriBuilder(
            Uri alpacaRestApi)
        {
            alpacaRestApi = alpacaRestApi ?? new Uri("https://api.alpaca.markets");

            var uriBuilder = new UriBuilder(alpacaRestApi)
            {
                Scheme = alpacaRestApi.Scheme == "http" ? "ws" : "wss"
            };

            if (!uriBuilder.Path.EndsWith("/", StringComparison.Ordinal))
            {
                uriBuilder.Path += "/";
            }

            uriBuilder.Path += "stream";
            return uriBuilder;
        }

        private void handleAuthorization(
            JsonAuthResponse response)
        {
            OnConnected(response.Status);

            if (response.Status == AuthStatus.Authorized)
            {
                var listenRequest = new JsonListenRequest
                {
                    Action = JsonAction.Listen,
                    Data = new JsonListenRequest.JsonData()
                    {
                        Streams = new List<String>
                        {
                            "trade_updates",
                            "account_updates"
                        }
                    }
                };

                SendAsJsonString(listenRequest);
            }
        }

        private void handleTradeUpdates(
            ITradeUpdate update)
        {
            OnTradeUpdate?.Invoke(update);
        }

        private void handleAccountUpdates(
            IAccountUpdate update)
        {
            OnAccountUpdate?.Invoke(update);
        }
    }
}
