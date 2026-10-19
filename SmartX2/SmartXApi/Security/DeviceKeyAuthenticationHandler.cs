using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SmartXApi.Security
{
    public static class DeviceKeyDefaults
    {
        public const string Scheme = "DeviceKey";
        public const string HeaderName = "X-Device-Key";
        public const string IngestPolicy = "TelemetryIngest";
    }

    public class DeviceKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IConfiguration _configuration;

        public DeviceKeyAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IConfiguration configuration)
            : base(options, logger, encoder)
        {
            _configuration = configuration;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(DeviceKeyDefaults.HeaderName, out var supplied) || string.IsNullOrWhiteSpace(supplied))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var expected = _configuration["DeviceIngest:ApiKey"];
            if (string.IsNullOrWhiteSpace(expected))
            {
                return Task.FromResult(AuthenticateResult.Fail("Device ingest key is not configured."));
            }

            var suppliedBytes = Encoding.UTF8.GetBytes(supplied.ToString());
            var expectedBytes = Encoding.UTF8.GetBytes(expected);

            if (!CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes))
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid device key."));
            }

            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "device-gateway"),
                    new Claim(ClaimTypes.Name, "device-gateway"),
                    new Claim(ClaimTypes.Role, "Device")
                },
                Scheme.Name);

            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
