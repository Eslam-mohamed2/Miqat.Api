namespace Miqat.API.Swagger;

/// <summary>
/// Two small scripts that run inside the Swagger page.
///
/// Without them, trying a protected endpoint means: run login, copy the token
/// out of the answer, open "Authorize", paste it - and the same again with the
/// refresh token for refresh and logout. With them, running login is enough.
///
/// HOW THESE STRINGS MUST BE WRITTEN. Swashbuckle places them inside
/// JSON.parse('...') in the page, and rebuilds the function from the text
/// between the first "{" and the last "}". So each one has to be:
///   - a single line,
///   - written with single quotes only (no double quotes, no backslashes),
///   - in the form: function (name) { ... }.
/// Break one of these and the whole Swagger page stops loading.
/// </summary>
public static class SwaggerUiScripts
{
    private const string StorageKey = "miqat.swagger.refreshToken";

    /// <summary>
    /// Before a request leaves the page: put the refresh token of the last
    /// sign-in where the example left a placeholder.
    /// </summary>
    public const string RequestInterceptor =
        "function (req) { " +
        "try { " +
        "var saved = window.localStorage.getItem('" + StorageKey + "'); " +
        "if (saved && typeof req.body === 'string' && req.body.indexOf('" + SwaggerExamplesFilter.RefreshTokenPlaceholder + "') !== -1) { " +
        "req.body = req.body.split('" + SwaggerExamplesFilter.RefreshTokenPlaceholder + "').join(saved); " +
        "} " +
        "} catch (e) { } " +
        "return req; }";

    /// <summary>
    /// After an answer arrives: when a sign-in endpoint returned tokens,
    /// authorize the page with the access token and remember the refresh token.
    /// </summary>
    public const string ResponseInterceptor =
        "function (res) { " +
        "try { " +
        "var data = res && (res.body || res.obj); " +
        "var url = (res && res.url) || ''; " +
        "if (res && res.ok && data && data.accessToken && url.indexOf('/api/Auth/') !== -1) { " +
        "if (data.refreshToken) { window.localStorage.setItem('" + StorageKey + "', data.refreshToken); } " +
        "if (window.ui && window.ui.preauthorizeApiKey) { window.ui.preauthorizeApiKey('Bearer', data.accessToken); } " +
        "} " +
        "} catch (e) { } " +
        "return res; }";
}
