using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Planning;
using Microsoft.SemanticKernel.Functions.OpenAPI.Extensions;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Planners;
using WPTTest.config;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web;

var builder = new KernelBuilder();

// Configure AI backend used by the kernel
var (useAzureOpenAI, model, azureEndpoint, apiKey, orgId) = Settings.LoadFromFile();

if (useAzureOpenAI)
{
    builder.WithAzureChatCompletionService(model, azureEndpoint, apiKey);
}
else
{
    builder.WithOpenAIChatCompletionService(model, apiKey, orgId);
}

var kernel = builder.Build();
//kernel.FunctionInvoked += (sender, args) =>
//{
//    Console.WriteLine($"Function {args.FunctionView.Name} invoked with parameters:");

//    foreach (ParameterView value in args.FunctionView.Parameters)
//    {
//        Console.WriteLine(value);
//    }
//};

//kernel.FunctionInvoking += (sender, args) =>
//{
//    Console.WriteLine($"Function {args.FunctionView.Name} invoking with parameters:");
//    foreach (ParameterView value in args.FunctionView.Parameters)
//    {
//        Console.WriteLine(value);
//    }
//};

// Add the math plugin using the plugin manifest URL
const string PluginManifestUrl = "https://localhost:5001/.well-known/ai-plugin.json";
var wptPlugin = await kernel.ImportPluginFunctionsAsync("WPTPlugin", new Uri(PluginManifestUrl), new OpenApiFunctionExecutionParameters { IgnoreNonCompliantErrors = true });
var bingConnector = new BingConnector("34f96ab90d3248eb826d92996f1846a9");
var webSearchEnginePlugin = new WebSearchEnginePlugin(bingConnector);

kernel.ImportFunctions(webSearchEnginePlugin, "WebSearch");
// Create a stepwise planner and invoke it
var plannerConfig = new StepwisePlannerConfig
{
    MaxTokens = 16384
};
var planner = new StepwisePlanner(kernel, plannerConfig);

var troubleshootQuery =
    """
      [Bot Instructions]:
      Provide the answer using the following format:
      {
          "insights" : "The problem is that the PC is {your insights from the result}. The next steps are {your recommendations} ",
          "facts" : {
              "fact1" : "value1",
              "fact2" : "value2"
          },
          "summary" : "The problem is {your insights in short}.",
      }

      In the insights field provide the result information in a clear way. You can use markdown for
      tables, bold, headers, etc. Provide the information so it will be easy to understand the result and deduce
      the required next steps. Explain how you came into the conclusion, which provider and function you used.
      Use only a json as in the previous format.
      Provide the facts that you used to reach the insights. The facts will be used for the next query.
      Make sure to include the pc and tenant ids in the facts array.
      The summary is a short description of the result.
      The result must be a json!

     [History]
     {{$history}}

     [Facts]
     {{$facts}}

     if tenant guid is not provided, you need to ask the user for it.
     if pc guid is not provided, you can query for all PCs in the tenant.

     Use the following tenant id: {{$tenantId}} and pc id: {{$pcId}} for diagnostics and pc information.

     User: {{$input}}
     """;

var variables = new ContextVariables
{
    ["input"] = "Show the latest blue screen errors from the last month, see the current date from the pc information, use the BSOD event id to filter. Explain how you found the errors. Show potential mitigation for the errors.",
    ["facts"] = "",
    ["history"] = "",
    ["tenantId"] = "7f8bfcae-90d2-4f2f-8914-0966ffd40786",
    ["pcId"] = "fe5da634-f0c6-4ef8-81bf-96369e8f04f9",
};

var plan = planner.CreatePlan(troubleshootQuery);
Console.WriteLine(plan.ToSafePlanString());

var result = await kernel.RunAsync(variables, plan);

// Print the results
Console.WriteLine("Result: " + result);

// Print details about the plan
if (result.FunctionResults.First().TryGetMetadataValue("stepCount", out string? stepCount))
{
    Console.WriteLine("Steps Taken: " + stepCount);
}

if (result.FunctionResults.First().TryGetMetadataValue("functionCount", out string? functionCount))
{
    Console.WriteLine("Functions Used: " + functionCount);
}

if (result.FunctionResults.First().TryGetMetadataValue("iterations", out string? iterations))
{
    Console.WriteLine("Iterations: " + iterations);
}

variables["history"] = result.ToString();
variables["input"] = "Use CDB to analyze the memory dump";

result = await kernel.RunAsync(variables, plan);

// Print the results
Console.WriteLine("Result: " + result);

variables["history"] = variables["history"] + result.ToString();
variables["input"] = "Search the web for how to solve the issue";
result = await kernel.RunAsync(variables, plan);
Console.WriteLine("Result: " + result);
