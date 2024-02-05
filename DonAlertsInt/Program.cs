using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Web;
using DonAlertsInt;
using DonationAlertsApiClient.Client;
using DonationAlertsApiClient.Client.Impl;
using DonationAlertsApiClient.Data;
using DonationAlertsApiClient.Factories.Impl;
using DonationAlertsApiClient.Services.Impl;
using Newtonsoft.Json;

string redirectUrl = WebUtility.UrlEncode("http://localhost:27629/auth");
string tokenRequestUrl = $"https://www.donationalerts.com/oauth/authorize?client_id=12138&response_type=code&redirect_uri={redirectUrl}&scope=oauth-user-show oauth-donation-subscribe";
var server = new HttpListener();
server.Prefixes.Add("http://127.0.0.1:27629/");
server.Start();

Console.WriteLine("Server started");

Task.Run(
    async () =>
    {
        await Task.Delay(100);
        await new HttpClient().GetAsync("http://localhost:27629/init");
        Console.WriteLine("Self initialized");
    });

var initWaiter = new TaskCompletionSource();

var donationQueue = new Queue<DonationAlertData>();
var resetEvent = new AutoResetEvent(false);

bool isAuthorizing = false;

var accessToken = "";


while (server.IsListening)
{
    var context = await server.GetContextAsync();

    var request = context.Request;

    if (request.Url!.LocalPath == "/init")
    {
        if (accessToken != "")
        {
            Console.WriteLine("Already authorized");
            var buffer = Encoding.UTF8.GetBytes(accessToken);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            await context.Response.OutputStream.FlushAsync();
            continue;
        }

        if (isAuthorizing)
        {
            await initWaiter.Task;
            var buffer = Encoding.UTF8.GetBytes(accessToken);
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            await context.Response.OutputStream.FlushAsync();
            continue;
        }

        // need to offload this routine, because it is thread-blocking
        Task.Run(
            async () =>
            {
                isAuthorizing = true;
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = tokenRequestUrl,
                        UseShellExecute = true
                    }
                );

                await initWaiter.Task;

                var buffer = accessToken.AsUtf8();
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer);
                await context.Response.OutputStream.FlushAsync();
            }
        );
    }
    else if (request.Url.LocalPath == "/auth")
    {
        var query = HttpUtility.ParseQueryString(request.Url!.Query);
        var code = query["code"];

        const string authUrl = "https://www.donationalerts.com/oauth/token";

        var client = new HttpClient();
        var content = new FormUrlEncodedContent(
            new Dictionary<string, string>()
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "12138",
                ["client_secret"] = "NIvvErCN2H9xYisLkNobzKUzpcYWoV9j6CUcC1Ot",
                ["redirect_uri"] = " http://localhost:27629/auth",
                ["code"] = code
            }
        );
        var response = await client.PostAsync(authUrl, content);
        response.EnsureSuccessStatusCode();
        var authResult = await response.Content.ReadFromJsonAsync<DonAlertsAuthResponse>();

        Console.WriteLine($"Authorized successfully. Got access_token");

        isAuthorizing = false;
        accessToken = authResult.AccessToken;
        initWaiter.SetResult();

        var buffer = "Success".AsUtf8();

        context.Response.ContentLength64 = buffer.Length;
        await using var output = context.Response.OutputStream;

        await output.WriteAsync(buffer);
        await output.FlushAsync();

        // ----
        // start listener

        var loggerService = new LoggerService();
        var donationAlertsApiServiceFactory = new DonationAlertsApiServiceFactory(loggerService, accessToken);
        var centrifugoServiceFactory = new CentrifugoServiceFactory(loggerService);
        var responseProcessingService = new ResponseProcessingService(loggerService);

        IDonationAlertsClient donationAlertsClient =
            new DonationAlertsClient(donationAlertsApiServiceFactory, centrifugoServiceFactory, responseProcessingService);

        try
        {
            await donationAlertsClient.Initialise();
            await donationAlertsClient.Connect();
            await donationAlertsClient.SubscribeToDonationAlerts();

            Console.WriteLine("Donation Alerts Centrifugo Listener Started");

            responseProcessingService.ReceivedDonationAlert += data =>
            {
                var donationSender = data.Username;
                var donationAmountSource = data.Amount;
                var donationCurrency = data.Currency;
                var donationAmountInMyCurrency = data.AmountInUserCurrency;
                var donationType = data.MessageType;
                var donationCreatedAt = data.CreatedAt;

                Console.WriteLine(
                    "Донат!\n" +
                    $"Отправитель: {donationSender ?? "(null)"}\n" +
                    $"Сумма (ориг): {donationAmountSource}\n" +
                    $"Валюта: {donationCurrency ?? "(null)"}\n" +
                    $"Сумма (руб): {donationAmountInMyCurrency}\n" +
                    $"Тип: {donationType ?? "(null)"}\n" +
                    $"Создано: {donationCreatedAt}\n"
                );

                donationQueue.Enqueue(data);
                resetEvent.Set();
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start centrifugo!\n{ex.Message}");
        }
    }
    else if (request.Url.LocalPath == "/events")
    {
        Task.Run(
            async () =>
            {
                await using var output = context.Response.OutputStream;

                if (donationQueue.Count == 0 && !resetEvent.WaitOne(5000))
                {
                    Console.WriteLine("Donation queue empty :(");
                    return;
                }
                if (donationQueue.TryDequeue(out var data))
                {
                    var donationSender = data.Username;
                    var donationAmountSource = data.Amount;
                    var donationCurrency = data.Currency;
                    var donationAmountInMyCurrency = data.AmountInUserCurrency;
                    var donationType = data.MessageType;
                    var donationCreatedAt = data.CreatedAt;

                    var buffer = JsonConvert.SerializeObject(
                            new DonationExportDto
                            {
                                Sender = donationSender,
                                AmountSource = donationAmountSource,
                                Currency = donationCurrency,
                                AmountInMyCurrency = donationAmountInMyCurrency,
                                Type = donationType,
                                CreatedAt = donationCreatedAt,
                            }
                        )
                        .AsUtf8();

                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.ContentType = "text/plain; charset=UTF-8";
                    // context.Response.ContentEncoding = Encoding.UTF8;

                    await output.WriteAsync(buffer);
                    await output.FlushAsync();

                    Console.WriteLine("Donation grabbed");
                }
            }
        );
    }
}

server.Stop();