using NzbDrone.Common.Http;
using TidalSharp.Data;

namespace TidalSharp;

// https://github.com/ta264/Lidarr.Plugin.Deemix/blob/master/src/Lidarr.Plugin.Deemix/Download/Clients/Deemix/DeemixProxy.cs#L218-L270
internal static class LidarrExtensions
{
    public static HttpRequestBuilder BuildRequest(this IHttpClient self, string baseUrl)
    {
        var builder =  new HttpRequestBuilder(baseUrl)
        {
            LogResponseContent = true,
            SuppressHttpError = true
        };
        return builder;
    }

    public static HttpResponse ProcessRequest(this IHttpClient self, HttpRequestBuilder requestBuilder)
    {
        var request = requestBuilder.Build();
        request.LogResponseContent = true;
        var response = self.Execute(request);
        return response;
    }

    public static async Task<HttpResponse> ProcessRequestAsync(this IHttpClient self, HttpRequestBuilder requestBuilder)
    {
        var request = requestBuilder.Build();
        request.LogResponseContent = true;
        var response = await self.ExecuteAsync(request);
        return response;
    }
}
