// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using Azure.ResourceManager.AppService.Models;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Samples.Common;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Sql.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ManageFunctionAppLogs
{
    public class Program
    {
        /**
         * Azure App Service basic sample for managing function apps.
         *  - Create a function app under the same new app service plan:
         *    - Deploy to app using FTP
         *    - stream logs for 30 seconds
         */

        public static async Task RunSample(ArmClient client)
        {
            // New resources
            AzureLocation region = AzureLocation.EastUS;
            string suffix         = ".azurewebsites.net";
            string appName       = Utilities.CreateRandomName("webapp1-");
            string app1Name      = Utilities.CreateRandomName("function-");
            string appUrl        = appName + suffix;
            string rgName        = Utilities.CreateRandomName("rg1NEMV_");
            var lro = await client.GetDefaultSubscription().GetResourceGroups().CreateOrUpdateAsync(Azure.WaitUntil.Completed, rgName, new ResourceGroupData(AzureLocation.EastUS));
            var resourceGroup = lro.Value;
            try {


                //============================================================
                // Create a function app with a new app service plan

                Utilities.Log("Creating function app " + appName + " in resource group " + rgName + "...");

                var webSiteCollection = resourceGroup.GetWebSites();
                var webSiteData = new WebSiteData(region)
                {
                    SiteConfig = new Azure.ResourceManager.AppService.Models.SiteConfigProperties()
                    {
                        WindowsFxVersion = "PricingTier.StandardS1",
                        NetFrameworkVersion = "NetFrameworkVersion.V4_6",
                    }
                };
                var webSite_lro = await webSiteCollection.CreateOrUpdateAsync(Azure.WaitUntil.Completed, appName, webSiteData);
                var webSite = webSite_lro.Value;

                var planCollection = resourceGroup.GetAppServicePlans();
                var planData = new AppServicePlanData(region)
                {
                };
                var planResource_lro = planCollection.CreateOrUpdate(Azure.WaitUntil.Completed, appName, planData);
                var planResource = planResource_lro.Value;

                SiteFunctionCollection functionAppCollection = webSite.GetSiteFunctions();
                var functionData = new FunctionEnvelopeData()
                {
                };
                var funtion_lro = functionAppCollection.CreateOrUpdate(Azure.WaitUntil.Completed, app1Name, functionData);
                var function = funtion_lro.Value;

                Utilities.Log("Created function app " + function.Data.Name);
                Utilities.Print(function);

                //============================================================
                // Deploy to app 1 through FTP

                Utilities.Log("Deploying a function app to " + appName + " through FTP...");

                var csm = new CsmPublishingProfile()
                {
                    Format = PublishingProfileFormat.Ftp
                };
                var stream_lro = await webSite.GetPublishingProfileXmlWithSecretsAsync(csm);
                var publishingprofile = stream_lro.Value;
                Utilities.UploadFileToFunctionApp(publishingprofile, Path.Combine(Utilities.ProjectPath, "Asset", "square-function-app", "host.json"));
                Utilities.UploadFileToFunctionApp(publishingprofile, Path.Combine(Utilities.ProjectPath, "Asset", "square-function-app", "square", "function.json"), "square/function.json");
                Utilities.UploadFileToFunctionApp(publishingprofile, Path.Combine(Utilities.ProjectPath, "Asset", "square-function-app", "square", "index.js"), "square/index.js");

                // sync triggers
                webSite.SyncFunctionTriggers();

                Utilities.Log("Deployment square app to web app " + webSite.Data.Name + " completed");
                Utilities.Print(webSite);

                // warm up
                Utilities.Log("Warming up " + appUrl + "/api/square...");
                Utilities.PostAddress("http://" + appUrl + "/api/square", "625");
                Thread.Sleep(1000);
                Utilities.Log("CURLing " + appUrl + "/api/square...");
                Utilities.Log(Utilities.PostAddress("http://" + appUrl + "/api/square", "625"));

                //============================================================
                // Listen to logs synchronously for 30 seconds

                using (var stream =stream_lro.Value)
                {
                    var reader = new StreamReader(stream);
                    Utilities.Log("Streaming logs from function app " + appName + "...");
                    string? line = reader.ReadLine();
                    Stopwatch stopWatch = new Stopwatch();
                    stopWatch.Start();
                    await Task.Factory.StartNew(() =>
                    {
                        Utilities.PostAddress("http://" + appUrl + "/api/square", "625");
                        Thread.Sleep(10000);
                        Utilities.PostAddress("http://" + appUrl + "/api/square", "725");
                        Thread.Sleep(10000);
                        Utilities.PostAddress("http://" + appUrl + "/api/square", "825");
                    });
                    while (line != null && stopWatch.ElapsedMilliseconds < 90000)
                    {
                        Utilities.Log(line);
                        line = reader.ReadLine();
                    }
                }
            }
            finally
            {
                try
                {
                    Utilities.Log("Deleting Resource Group: " + rgName);
                    await resourceGroup.DeleteAsync(WaitUntil.Completed);
                    Utilities.Log("Deleted Resource Group: " + rgName);
                }
                catch (NullReferenceException)
                {
                    Utilities.Log("Did not create any resources in Azure. No clean up is necessary");
                }
                catch (Exception g)
                {
                    Utilities.Log(g);
                }
            }
        }

        public static async Task Main(string[] args)
        {
            try
            {
                //=================================================================
                // Authenticate
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");
                var tenantId = Environment.GetEnvironmentVariable("TENANT_ID");
                var subscription = Environment.GetEnvironmentVariable("SUBSCRIPTION_ID");
                ClientSecretCredential credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                ArmClient client = new ArmClient(credential, subscription);

                // Print selected subscription
                Utilities.Log("Selected subscription: " + client.GetSubscriptions().Id);

                await RunSample(client);
            }
            catch (Exception e)
            {
                Utilities.Log(e);
            }
        }
    }
}