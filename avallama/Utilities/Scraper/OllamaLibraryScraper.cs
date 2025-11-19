// Copyright (c) Márk Csörgő and Martin Bartos
// Licensed under the MIT License. See LICENSE file for details.

using avallama.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace avallama.Utilities.Scraper;

// TODO: Skip cloud models ("cloud" in name or labels)

public static class OllamaLibraryScraper
{
    private const string OllamaUrl = "https://www.ollama.com";
    private static readonly HttpClient HttpClient;
    private static readonly SemaphoreSlim Throttler;

    private static readonly TokenBucketRateLimiterOptions RateLimiterOptions = new()
    {
        TokenLimit = 5,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 15,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true
    };

    static OllamaLibraryScraper()
    {
        Throttler = new SemaphoreSlim(10);
        HttpClient = new HttpClient(
            handler: new OllamaRateLimitedHandler(
                new TokenBucketRateLimiter(RateLimiterOptions)
            )
        );
    }

    public static async Task<OllamaLibraryScraperResult> GetAllOllamaModelsAsync()
    {
        var families = await GetOllamaFamiliesAsync();
        var channel = Channel.CreateUnbounded<OllamaModel>();

        var tasks = families.Select(async family =>
        {
            await Throttler.WaitAsync();
            try
            {
                await foreach (var model in GetOllamaModelsFromFamilyAsync(family))
                {
                    await channel.Writer.WriteAsync(model);
                }
            }
            finally
            {
                Throttler.Release();
            }
        }).ToList();

        // this uses fire and forget on purpose, if we awaited this task, streaming would not work because it would wait for all producers to finish
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            finally
            {
                channel.Writer.Complete();
            }
        });

        return new OllamaLibraryScraperResult()
        {
            Models = StreamModels(),
            Families = families
        };

        async IAsyncEnumerable<OllamaModel> StreamModels()
        {
            await foreach (var model in channel.Reader.ReadAllAsync())
            {
                yield return model;
            }
        }
    }

    private static async Task<List<OllamaModelFamily>> GetOllamaFamiliesAsync()
    {
        HttpResponseMessage response;
        try
        {
            response = await HttpClient.GetAsync(OllamaUrl + "/library");
        }
        catch (HttpRequestException)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var html = await response.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var result = new List<OllamaModelFamily>();

        var nodeSelector = doc.DocumentNode.SelectNodes("//*[@id='repo']/ul/li/a");

        foreach (var aNode in nodeSelector)
        {
            try
            {
                var family = new OllamaModelFamily();

                var nameNode = aNode.SelectSingleNode("div/h2/div/span");
                family.Name = nameNode.InnerText.Trim();

                var descNode = aNode.SelectSingleNode("div/p");
                family.Description = descNode.InnerText.Trim();

                // Pull count: “div:nth-of-type(2) > p > span:first-of-type > span:first-of-type”
                var secondDiv = aNode.SelectSingleNode("div[2]");
                var pInSecondDiv = secondDiv.SelectSingleNode("p");
                var span1 = pInSecondDiv.SelectSingleNode("span");
                var span1Inner = span1.SelectSingleNode("span");
                var pullText = span1Inner.InnerText.Trim();

                family.PullCount = ConversionHelper.ParseAbbreviatedNumber(pullText);

                var spanTagCount = pInSecondDiv.SelectSingleNode("span[2]/span");
                if (int.TryParse(spanTagCount.InnerText.Trim(), out var tagCount))
                    family.TagCount = tagCount;

                // Last updated: third span etc.
                var lastUpdatedNode = pInSecondDiv.SelectSingleNode("span[3]");
                var lastUpdated = lastUpdatedNode.GetAttributeValue("title", string.Empty);
                family.LastUpdated = ConversionHelper.ParseUtcDate(lastUpdated);

                // Popular tags: list of span under div > div > span
                // e.g., tags are shown in some nested span set
                var labelSpanNodes = aNode.SelectNodes("div/div/span");

                var labels = labelSpanNodes.Select(labelSpan => labelSpan.InnerText.Trim())
                    .Where(t => !string.IsNullOrEmpty(t)).ToList();

                family.Labels = labels;
                result.Add(family);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GetOllamaFamiliesAsync - ERROR] {ex.Message}");
                // TODO: proper logging
            }
        }

        return result;
    }

    private static async IAsyncEnumerable<OllamaModel> GetOllamaModelsFromFamilyAsync(OllamaModelFamily family)
    {
        var familyUrl = $"{OllamaUrl}/library/{family.Name}/tags";
        HtmlNodeCollection? tagNodes = null;
        try
        {
            var tagsResponse = await HttpClient.GetAsync(familyUrl);
            if (tagsResponse.IsSuccessStatusCode)
            {
                var tagsHtml = await tagsResponse.Content.ReadAsStringAsync();
                var tagsDoc = new HtmlDocument();
                tagsDoc.LoadHtml(tagsHtml);

                // body/div/section/div/div/all divs starting from 2
                tagNodes = tagsDoc.DocumentNode.SelectNodes("//section/div/div/div");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetOllamaModelsFromFamilyAsync - ERROR] {ex.Message}");
            // TODO: proper logging
        }

        if (tagNodes is null) yield break;

        // first one is skipped, because they're column names
        for (var i = 1; i < tagNodes.Count; i++)
        {
            // name, size, context (available only from /library/{family_name}/tags) iterating through a list of models (as div elements)
            HtmlNode? tagNameNode = tagNodes[i].SelectSingleNode("a/div/div/div/span");
            var tagName = tagNameNode.InnerText.Trim() ?? string.Empty;

            HtmlNode? tagSizeNode = tagNodes[i].SelectSingleNode("div/div[1]/p[1]");
            var tagSize = tagSizeNode.InnerText.Trim() ?? "0";

            var model = new OllamaModel
            {
                Name = tagName,
                Size = ConversionHelper.ParseSizeToBytes(tagSize),
                Family = family
            };

            yield return model;
        }
    }

    public sealed class OllamaLibraryScraperResult
    {
        public required IAsyncEnumerable<OllamaModel> Models { get; init; }
        public required IList<OllamaModelFamily> Families { get; init; }
    }
}
