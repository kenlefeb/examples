﻿// Copyright 2016-2019, Pulumi Corporation.  All rights reserved.

using Pulumi;
using Pulumi.Azure.AppInsights;
using Pulumi.Azure.AppService;
using Pulumi.Azure.AppService.Inputs;
using Pulumi.Azure.Bot;
using Pulumi.Azure.Cognitive.Inputs;
using Pulumi.Azure.Core;
using Pulumi.AzureAD;
using Pulumi.Random;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cognitive = Pulumi.Azure.Cognitive;
using Storage = Pulumi.Azure.Storage;

class Program
{
    static Task<int> Main(string[] args)
    {
        return Deployment.RunAsync(() =>
        {
            var config = new Config();
            var botName = config.Require("botName");

            var resourceGroup = new ResourceGroup("botservice-rg");

            var storageAccount = new Storage.Account("sa", new Storage.AccountArgs
            {
                ResourceGroupName = resourceGroup.Name,
                AccountReplicationType = "LRS",
                AccountTier = "Standard"
            });

            var appServicePlan = new Plan("asp", new PlanArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Kind = "App",
                Sku = new PlanSkuArgs
                {
                    Tier = "Basic",
                    Size = "B1"
                },
            });

            var container = new Storage.Container("zips", new Storage.ContainerArgs
            {
                StorageAccountName = storageAccount.Name,
                ContainerAccessType = "private",
            });

            var blob = new Storage.ZipBlob("zip", new Storage.ZipBlobArgs
            {
                StorageAccountName = storageAccount.Name,
                StorageContainerName = container.Name,
                Type = "block",
                Content = new FileArchive("bot/publish")
            });

            var codeBlobUrl = SharedAccessSignature.SignedBlobReadUrl(blob, storageAccount);

            var appInsights = new Insights("ai", new InsightsArgs
            {
                ApplicationType = "web",
                ResourceGroupName = resourceGroup.Name
            });

            var appInsightApiKey = new ApiKey("ai", new ApiKeyArgs
            {
                ApplicationInsightsId = appInsights.Id,
                ReadPermissions = "api",
            });

            var luis = new Cognitive.Account("cs", new Cognitive.AccountArgs
            {
                Kind = "CognitiveServices", // includes LUIS
                ResourceGroupName = resourceGroup.Name,
                Sku = new AccountSkuArgs { Name = "S0", Tier = "Standard" }
            });

            var msa = new Application("msapp", new ApplicationArgs
            {
                Oauth2AllowImplicitFlow = false,
                AvailableToOtherTenants = true,
                PublicClient = true
            });

            var pwd = new RandomPassword("password", new RandomPasswordArgs
            {
                Length = 16,
                MinNumeric = 1,
                MinSpecial = 1,
                MinUpper = 1,
                MinLower = 1
            });

            var msaSecret = new ApplicationPassword("msasecret", new ApplicationPasswordArgs
            {
                ApplicationObjectId = msa.ObjectId,
                EndDateRelative = "8640h",
                Value = pwd.Result
            });

            var app = new AppService("app", new AppServiceArgs
            {
                ResourceGroupName = resourceGroup.Name,
                AppServicePlanId = appServicePlan.Id,
                AppSettings =
                {
                    { "WEBSITE_RUN_FROM_PACKAGE", codeBlobUrl },
                    { "MicrosoftAppId", msa.ApplicationId },
                    { "MicrosoftAppPassword", msaSecret.Value },
                    { "LuisApiKey", luis.PrimaryAccessKey },
                },
                HttpsOnly = true
            });

            var bot = new WebApp(botName, new WebAppArgs
            {
                DisplayName = botName,
                MicrosoftAppId = msa.ApplicationId,
                ResourceGroupName = resourceGroup.Name,
                Sku = "F0",
                Location = "global",
                Endpoint = Output.Format($"https://{app.DefaultSiteHostname}/api/messages"),
                DeveloperAppInsightsApiKey = appInsightApiKey.Key,
                DeveloperAppInsightsApplicationId = appInsights.AppId,
                DeveloperAppInsightsKey = appInsights.InstrumentationKey
            });

            return new Dictionary<string, object>
            {
                { "Bot Endpoint", bot.Endpoint },
                { "MicrosoftAppId", msa.ApplicationId },
                { "MicrosoftAppPassword", msaSecret.Value }
            };
        });
    }
}
