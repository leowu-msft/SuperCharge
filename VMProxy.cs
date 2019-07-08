using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Common;
using System.Net.Http;
using Microsoft.TeamFoundation.Build.WebApi;
using System.Collections.Generic;
using System.Linq;
using System;

namespace LeoWu.Function
{
    public static class VMProxy
    {
        private static string tfsAccount = System.Environment.GetEnvironmentVariable("VstsAccount");
        private static string tfsProjectCollection = System.Environment.GetEnvironmentVariable("VstsProjectCollection");
        private static string tfsProject = System.Environment.GetEnvironmentVariable("VstsProject");
        private static string tfsToken = System.Environment.GetEnvironmentVariable("VstsToken");
        private static int tfsWindowsBuildDefId = Convert.ToInt32(System.Environment.GetEnvironmentVariable("VstsWindowsBuildDefId"));
        private static int tfsRedhatBuildDefId = Convert.ToInt32(System.Environment.GetEnvironmentVariable("VstsRedhatBuildDefId"));

        /// <summary>
        /// Get Vss connection 
        /// </summary>
        /// <param name="url">url to TFS instance</param>
        /// <param name="pat">Personal Access Token</param>
        /// <returns></returns>
        private static async Task<VssConnection> GetVssConnectionAsync(string url, string pat)
        {
            var conn = new VssConnection(new Uri(url), 
                                         new VssCredentials(new Microsoft.VisualStudio.Services.Common.VssBasicCredential(string.Empty, pat)));
            await conn.ConnectAsync();

            return conn;    
        }
        
        /// <summary>
        /// Queue new build 
        /// </summary>
        /// <param name="conn">VSS connection</param>
        /// <param name="project">Project name</param>
        /// <param name="definitionID">Build pipeline ID</param>
        /// <param name="parameters">Parameters from query string</param>
        /// <returns></returns>
        private static async Task<QueuedBuildResult> SubmitNewBuildAsync(VssConnection conn, string project, int definitionId, string parameters)
        {
            var client = conn.GetClient<BuildHttpClient>();

            // Get the build definition 
            var definition = await client.GetDefinitionAsync(project, definitionId);

            // Queue a new build 
            var build = new Build
            {
                Definition = new DefinitionReference
                {
                    Id = definition.Id
                },
                Project = definition.Project,
                Parameters = parameters
            };
            var result = await client.QueueBuildAsync(build);

            // Return QuquedBuildResult class to caller 
            return new QueuedBuildResult {
                BuildNumber = result.BuildNumber,
                Uri = new System.Uri(((Microsoft.VisualStudio.Services.WebApi.ReferenceLink)result.Links.Links["web"]).Href)
            };
        }

        /// <summary>
        /// Run method for this http trigger 
        /// </summary>
        /// <param name="req">http request object</param>
        /// <param name="log">Ilogger object for logging</param>
        /// <returns></returns>
        [FunctionName("VMProxy")]
        public static async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("VMProxy HTTP trigger function processed a request.");

            var qryItems = new Dictionary<string, string>()
            {
                { "RequestEnvironment","os" },
                { "RequestAlias","alias" }
            };

            // Validate supplied parameters
            if (!qryItems.All(k => req.Query.ContainsKey(k.Value)))
            {
                return new BadRequestObjectResult("Please pass required parameters on the query string");
            }

            var entries = qryItems.Select(d => string.Format("\"{0}\": \"{1}\"", d.Key, req.Query[d.Value]));
            var buildParams = "{" + string.Join(",", entries) + "}";

            log.LogInformation(buildParams);

            // Initiating a connection 
            string url = $"https://dev.azure.com/{tfsAccount}";            
            var tfsConn = await GetVssConnectionAsync(url, tfsToken);

            if(tfsConn != null && tfsConn.HasAuthenticated)
            {
                log.LogInformation("Tfs connection is established ");
            }
            else
            {
                return new BadRequestObjectResult("Connection to  '" + url + "' is failed.  Please verify authenticated token value and try again");
            }

            // Determine reqested environment - Windows or Redhat 
            var tfsBuildID = tfsWindowsBuildDefId;
            if(req.Query["os"] == "Redhat")
            {
                tfsBuildID = tfsRedhatBuildDefId;
            }
            else
            {
                tfsBuildID = tfsWindowsBuildDefId;
            }

            // Queue a new build 
            var buildResult = await SubmitNewBuildAsync(tfsConn, tfsProject, tfsBuildID, buildParams);

            // return build result 
            if (buildResult == null)
            {
                return new BadRequestObjectResult("The kickoff of build definition " + tfsBuildID + " is not successful");
            }
            else
            {
                return new JsonResult(buildResult);
            }
        }
    }

    public class QueuedBuildResult
    {
        public string BuildNumber { get; set; }
        public Uri Uri { get; set; }
    }
}
