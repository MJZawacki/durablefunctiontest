using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;


namespace durableTest
{
    public static class trigger
    {

        [FunctionName("trigger")]
        public static async Task<List<string>> RunOrchestrator(
            [OrchestrationTrigger] DurableOrchestrationContext ctx)
        {

            var retryOptions = new RetryOptions(
                firstRetryInterval: TimeSpan.FromSeconds(5),
                maxNumberOfAttempts: 3
            );

            TimeSpan timeout = TimeSpan.FromSeconds(60);
            DateTime deadline = ctx.CurrentUtcDateTime.Add(timeout);
            
            var outputs = new List<string>();
            var parallelTasks = new List<Task<string>>();
            List<string> results = null;
            using(var cts = new CancellationTokenSource())
            {
                // get a list of N work items to process in parallel
                for (int i = 0; i < 200; i++)
                {
                        Task<string> task = ctx.CallActivityWithRetryAsync<string>("trigger_Hello", retryOptions, i.ToString());

                    
                    parallelTasks.Add(task);
                }
                Task timeoutTask = ctx.CreateTimer(deadline, cts.Token);
                
                Task runTask =  Task.WhenAll(parallelTasks);
                Task winner = await Task.WhenAny(runTask, timeoutTask);
                if (winner == runTask)
                {
                    // Success case
                    cts.Cancel();
                    results = parallelTasks.Select(t => t.Result).ToList();
                }
            }
            // aggregate all N outputs and send result to F3
            
            return results;
        }

        [FunctionName("trigger_Hello")]
        public static string SayHello([ActivityTrigger] string name, ILogger log)
        {
            log.LogInformation($"Saying hello to {name}.");
            return $"Hello {name}!";
        }

        [FunctionName("trigger_HttpStart")]
        public static async Task<HttpResponseMessage> HttpStart(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")]HttpRequestMessage req,
            [OrchestrationClient]DurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("trigger", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }
    }
}