using System.IO;
using System.Text.RegularExpressions;

var path = "../ohrbob2/Ohrbob.Player/Controllers/StreamController.cs";
var text = File.ReadAllText(path);

text = text.Replace(@"using MongoDB.Driver;", @"using MongoDB.Driver;
using Ohrbob.Server.Shared.Requests;");

text = text.Replace(@"var dtos = stations.Select(s => new
        {
            id = s.Id,
            name = s.Name,
            feeds = s.Feeds?.Select(fUrl => 
            {
                var f = allFeeds.FirstOrDefault(x => x.Url == fUrl);
                return new { imageUrl = f?.ImageUrl };
            }).Where(f => !string.IsNullOrEmpty(f.imageUrl)).ToList()
        });

        return Ok(dtos);", @"var dtos = stations.Select(s => new StationWithFeedsDto
        {
            Station = s,
            Feeds = s.Feeds?.Select(fUrl => 
            {
                var f = allFeeds.FirstOrDefault(x => x.Url == fUrl);
                return new FeedStatsDto { Url = fUrl, ImageUrl = f?.ImageUrl };
            }).ToList() ?? new System.Collections.Generic.List<FeedStatsDto>()
        });

        return Ok(dtos);");

File.WriteAllText(path, text);
