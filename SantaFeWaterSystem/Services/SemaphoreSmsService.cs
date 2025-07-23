using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using SantaFeWaterSystem.Settings;

namespace SantaFeWaterSystem.Services
{
    public class SemaphoreSmsService : ISemaphoreSmsService
    {
        private readonly SemaphoreSettings _settings;

        public SemaphoreSmsService(IOptions<SemaphoreSettings> options)
        {
            _settings = options.Value;
        }

        public async Task<(bool success, string response)> SendSmsAsync(string number, string message)
        {
            using var client = new HttpClient();
            var data = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("apikey", _settings.ApiKey),
                new KeyValuePair<string, string>("number", number),
                new KeyValuePair<string, string>("message", message),
                new KeyValuePair<string, string>("sendername", _settings.SenderName)
            });

            var response = await client.PostAsync("https://api.semaphore.co/api/v4/messages", data);
            var responseBody = await response.Content.ReadAsStringAsync();
            return (response.IsSuccessStatusCode, responseBody);
        }
    }
}
