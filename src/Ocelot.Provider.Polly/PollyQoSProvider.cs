namespace Ocelot.Provider.Polly
{
    using System;
    using System.Net;
    using System.Net.Http;
    using global::Polly;
    using global::Polly.CircuitBreaker;
    using global::Polly.Timeout;
    using Ocelot.Configuration;
    using Ocelot.Logging;

    public class PollyQoSProvider
    {
        private readonly IOcelotLogger _logger;
        private readonly IAsyncPolicy<HttpResponseMessage>[] _policies;

        public PollyQoSProvider(DownstreamReRoute reRoute, IOcelotLoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<PollyQoSProvider>();

            Enum.TryParse(reRoute.QosOptions.TimeoutStrategy, out TimeoutStrategy strategy);
            var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromMilliseconds(reRoute.QosOptions.TimeoutValue), strategy);

            var circuitBreakerPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<TimeoutRejectedException>()
                .Or<TimeoutException>()
                .OrResult<HttpResponseMessage>(response =>
                {
                    if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        return true;
                    }
                    return false;
                })
                .CircuitBreakerAsync(reRoute.QosOptions.ExceptionsAllowedBeforeBreaking,
                    TimeSpan.FromMilliseconds(reRoute.QosOptions.DurationOfBreak),
                    onBreak: (ex, breakDelay) =>
                    {
                        _logger.LogError(
                            ".Breaker logging: Breaking the circuit for " + breakDelay.TotalMilliseconds + "ms!", ex.Exception);
                    },
                    onReset: () =>
                    {
                        _logger.LogDebug(".Breaker logging: Call ok! Closed the circuit again.");
                    },
                    onHalfOpen: () =>
                    {
                        _logger.LogDebug(".Breaker logging: Half-open; next call is a trial.");
                    }
                );

            _policies=new IAsyncPolicy<HttpResponseMessage>[]{circuitBreakerPolicy,timeoutPolicy};

        }

        public IAsyncPolicy<HttpResponseMessage>[] Policies => _policies;
    }
}
