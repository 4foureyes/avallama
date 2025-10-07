using System;
using System.Threading.Tasks;
using Xunit;

namespace avallama.Tests.Extensions
{

    // Extension class to enable a 'DoesNotThrowAsync' assertion in testing
    public static class AsyncAssertExtensions
    {
        public static async Task DoesNotThrowAsync(Func<Task> testCode)
        {
            try
            {
                await testCode();
            }
            catch (Exception ex)
            {
                Assert.Fail($"Expected no exception, but got: {ex}");
            }
        }

        public static async Task<T> DoesNotThrowAsync<T>(Func<Task<T>> testCode)
        {
            try
            {
                return await testCode();
            }
            catch (Exception ex)
            {
                Assert.Fail($"Expected no exception, but got: {ex}");
                return default!; // will never be reached, compiler needs a return
            }
        }
    }
}
