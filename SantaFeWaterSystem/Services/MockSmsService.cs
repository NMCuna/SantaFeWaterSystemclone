using System;
using System.Threading.Tasks;

namespace SantaFeWaterSystem.Services
{
    public class MockSmsService : ISemaphoreSmsService
    {
        public Task<(bool success, string response)> SendSmsAsync(string number, string message)
        {
            Console.WriteLine($"[MOCK SMS] To: {number} | Message: {message}");
            return Task.FromResult((true, "Mock SMS sent successfully"));
        }
    }
}
