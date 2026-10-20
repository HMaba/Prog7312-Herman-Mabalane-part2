using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartxWPF.Services;

public sealed class AuthenticationService(ApiClient apiClient)
{
    // Firebase Web API keys identify a project; they are not service-account credentials.
    private const string FirebaseApiKey = "AIzaSyAiP5t-52W9Q3Z9kFKc9vGbx4oi5nB4nCM";
    private static readonly HttpClient FirebaseHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    public string? SignedInEmail { get; private set; }

    public async Task SignInAsync(string apiBaseUrl, string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Enter your Firebase email address.", nameof(email));
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Enter your Firebase password.", nameof(password));

        var endpoint = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={FirebaseApiKey}";
        using var response = await FirebaseHttp.PostAsJsonAsync(endpoint, new FirebaseSignInRequest(email.Trim(), password, true), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var code = "AUTHENTICATION_FAILED";
            try
            {
                using var document = JsonDocument.Parse(body);
                code = document.RootElement.GetProperty("error").GetProperty("message").GetString() ?? code;
            }
            catch (JsonException) { }
            throw new InvalidOperationException(FriendlyFirebaseError(code));
        }

        var result = JsonSerializer.Deserialize<FirebaseSignInResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        { PropertyNameCaseInsensitive = true });
        if (result is null || string.IsNullOrWhiteSpace(result.IdToken))
            throw new InvalidOperationException("Firebase sign-in succeeded but did not return an ID token.");

        apiClient.Configure(apiBaseUrl, result.IdToken);
        SignedInEmail = result.Email ?? email.Trim();
    }

    public void SignOut(string apiBaseUrl)
    {
        apiClient.Configure(apiBaseUrl, null);
        SignedInEmail = null;
    }

    private static string FriendlyFirebaseError(string code) => code switch
    {
        "EMAIL_NOT_FOUND" or "INVALID_LOGIN_CREDENTIALS" => "Firebase could not find an account matching that email and password.",
        "INVALID_PASSWORD" => "The Firebase password is incorrect.",
        "USER_DISABLED" => "This Firebase account has been disabled.",
        "OPERATION_NOT_ALLOWED" => "Email/password sign-in is disabled. Enable Email/Password in Firebase Console → Authentication → Sign-in method.",
        "TOO_MANY_ATTEMPTS_TRY_LATER" => "Firebase has temporarily limited sign-in attempts. Try again later.",
        "INVALID_EMAIL" => "Enter a valid email address.",
        "API_KEY_INVALID" => "The Firebase Web API key was rejected. Verify the Firebase project configuration.",
        _ => $"Firebase sign-in failed ({code}). Check the Firebase Authentication settings and account."
    };

    private sealed record FirebaseSignInRequest(string Email, string Password, bool ReturnSecureToken);
    private sealed record FirebaseSignInResponse(string IdToken, string Email, string RefreshToken, string LocalId, string ExpiresIn);
}
