using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace GrandTheftMultiplayer.Launcher.Helpers
{
    public class HttpDataHelper
    {
        public static async Task<T> GetDataFromUrl<T>(string url) where T : class
        {
            try
            {
                // The shared client avoids a new connection pool per request, which the
                // status poller would otherwise exhaust after a few minutes.
                var response = await HttpClientProvider.Client.GetAsync(url);

                return response.IsSuccessStatusCode
                    ? JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync())
                    : null;
            }
            catch (Exception)
            {
                // Callers treat a null result as "no data available".
            }

            return default(T);
        }
    }
}
