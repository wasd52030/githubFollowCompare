using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Configuration;
using System.Net.Mail;
using System.Net;
using System;
using System.CommandLine;

Console.OutputEncoding = System.Text.Encoding.UTF8;

async Task<List<FollowRecord>> GetfollowInfo(FollowInfoType type, string apiKey, string user)
{
    var client = new HttpClient();
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");

    var apiUri = new UriBuilder($"https://api.github.com/users/{user}/{type}");
    int page = 1;
    var queryParams = HttpUtility.ParseQueryString(apiUri.Query);
    queryParams["per_page"] = 50.ToString();
    queryParams["page"] = page.ToString();
    apiUri.Query = queryParams.ToString();


    var followInfo = new List<FollowRecord>();

    while (true)
    {
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = apiUri.Uri,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (responseBody.Replace("\n", "") == "[]")
        {
            break;
        }

        using JsonDocument json = JsonDocument.Parse(responseBody, new JsonDocumentOptions { AllowTrailingCommas = true });

        foreach (var githubUser in json.RootElement.EnumerateArray())
        {
            var login = githubUser.GetProperty("login").GetString();
            githubUser.GetProperty("id").TryGetInt32(out int id);
            var node_id = githubUser.GetProperty("node_id").GetString();
            var html_url = githubUser.GetProperty("html_url").GetString();

            followInfo.Add(new FollowRecord(login!, id, node_id!, html_url!));
        }

        page++;
        queryParams["page"] = page.ToString();
        apiUri.Query = queryParams.ToString();
    }

    return followInfo;
}


// reference -> https://blog.hungwin.com.tw/cs-gmail/
void MailNotify(Configure config, string targetAccount, IEnumerable<FollowRecord> diff)
{
    try
    {
        var to = config.ToEmail;
        string SmtpServer = "smtp.gmail.com";
        int SmtpPort = 587;
        using var msg = new MailMessage
        {
            From = new MailAddress(config.SenderEmail),
            Subject = $"[github - {targetAccount}] follower - following compare",
            Body = $"current time = {DateTime.Now}<br>{string.Join("<br>", diff)}",
            IsBodyHtml = true,
            SubjectEncoding = Encoding.UTF8
        };
        msg.To.Add(new MailAddress(to));

        using (var mailClient = new SmtpClient(SmtpServer, SmtpPort))
        {
            mailClient.EnableSsl = true;
            mailClient.Credentials = new NetworkCredential(config.SenderEmail, config.SenderTempPwd);
            mailClient.Send(msg);
        }
    }
    catch (System.Exception)
    {
        throw;
    }
}

async Task Invoke(Configure config, string targetAccount)
{
    var watch = new Stopwatch();
    watch.Start();
    var followers = await GetfollowInfo(FollowInfoType.followers, config.ApiKey, targetAccount);
    var followings = await GetfollowInfo(FollowInfoType.following, config.ApiKey, targetAccount);

    Console.WriteLine();

    // reference -> https://thesoftwarearchitect.com/net-6-intersectby-and-exceptby/
    var diff = followers.ExceptBy(followings.Select(x => x.LoginAccount), record => record.LoginAccount);
    foreach (var r in diff)
    {
        Console.WriteLine(r);
    }
    MailNotify(config, targetAccount, diff);

    watch.Stop();
    Console.WriteLine(watch.Elapsed);
}

async Task Main()
{
    IConfiguration configuration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .Build();

    var config = new Configure()
    {
        ApiKey = Environment.GetEnvironmentVariable("GithubAPIKey") ?? configuration.GetValue<string>("GithubAPIKey")!,
        SenderEmail = Environment.GetEnvironmentVariable("senderEmail") ?? configuration.GetValue<string>("sender:email")!,
        SenderTempPwd = Environment.GetEnvironmentVariable("senderTempPwd") ?? configuration.GetValue<string>("sender:TempPwd")!,
        ToEmail = Environment.GetEnvironmentVariable("ToEmail") ?? configuration.GetValue<string>("to:email")!,
    };


    var rootCommand = new RootCommand("github follower - following compare");


    var TargetAccountOption = new Option<string>
    (aliases: new string[] { "--targetAccount" },
    description: "github account you want to query",
    getDefaultValue: () => "wasd52030");

    // collect command
    var collectCommand = new Command(name: "collect", description: "統整github follwer與following的差別")
    {
        TargetAccountOption
    };
    rootCommand.AddCommand(collectCommand);

    collectCommand.SetHandler(async (TargetAccountOption) =>
    {
        await Invoke(config, TargetAccountOption);
    }, TargetAccountOption);

    await rootCommand.InvokeAsync(args);
}

await Main();