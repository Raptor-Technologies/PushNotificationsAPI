using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.NotificationHubs;
using Microsoft.Extensions.Options;
using PushNotificationsAPI.Models;
using System.Net;
namespace PushNotificationsAPI.Services;

public class NotificationHubService : INotificationService
{
	public static string FcmTemplateBody { get; } = "{\"message\":{\"android\":{\"data\":{\"json\":\"$(jsonPayloadParam)\",\"title\":\"$(titleParam)\",\"body\":\"$(bodyParam)\",\"critical\":\"false\"}}}}";
	public static string FcmCriticalTemplateBody { get; } = "{\"message\":{\"android\":{\"data\":{\"json\":\"$(jsonPayloadParam)\",\"title\":\"$(titleParam)\",\"body\":\"$(bodyParam)\",\"critical\":\"true\",\"sound\":\"$(soundParam)\"},\"priority\":\"high\"}}}";

	public static string ApnTemplateBody { get; } = "{\"aps\": {\"alert\" : { \"title\" : \"$(titleParam)\", \"body\" : \"$(bodyParam)\" }, \"sound\":\"default\", \"json\":\"$(jsonPayloadParam)\"}}";
	public static string ApnCriticalTemplateBody { get; } = "{\"aps\": {\"alert\" : { \"title\" : \"$(titleParam)\", \"body\" : \"$(bodyParam)\" }, \"sound\" : {\"critical\" : 1, \"volume\" : 1.0, \"name\" : \"$(iosSoundParam)\"}, \"json\":\"$(jsonPayloadParam)\"}}";

	public static string LegacyFcmTemplateBody { get; } = "{\"message\":{\"android\":{\"data\":{\"json\":\"$(jsonPayloadParam)\",\"title\":\"$(titleParam)\",\"body\":\"$(bodyParam)\",\"critical\":\"$(criticalParam)\",\"sound\":\"$(soundParam)\"},\"priority\":\"$(priorityLParam)\"}}}";
	public static string LegacyApnTemplateBody { get; } = "{\"aps\":{\"alert\":{\"title\":\"$(titleParam)\", \"body\":\"$(bodyParam)\"}, \"sound\":\"default\", \"json\":\"$(jsonPayloadParam)\"}}";


	readonly NotificationHubClient _hub;

	public NotificationHubService(IOptions<NotificationHubOptions> options, IConfiguration configuration)
	{
		_hub = NotificationHubClient.CreateClientFromConnectionString(configuration["ConnectionStrings:AzureHubConnectionString"], configuration["ConnectionStrings:AzureHubName"]);
	}

	public async Task<object> GetRegistrationAsync(string tag)
	{
		var registrations = await _hub.GetRegistrationsByChannelAsync(tag.ToUpper(), 100);
		return registrations;
	}

	public async Task<bool> RequestNotificationAsync(NotificationRequest notificationRequest, CancellationToken token)
	{
		var mTags = notificationRequest.Tags;

		try
		{
			if (mTags.Length > 0)
			{
				Dictionary<string, string> templateParameters = new Dictionary<string, string>
				{
					["titleParam"] = notificationRequest.Title,
					["bodyParam"] = notificationRequest.Body,
					["jsonPayloadParam"] = notificationRequest.Json,
					["soundParam"] = (notificationRequest.Sound.ToString() ?? "alert"),
					["iosSoundParam"] = (notificationRequest.Sound.ToString() ?? "alert") + ".wav", //keep it separated so we dont require an app update
				};

				//create expression by batches of 20 tags (max allowed by AHN) codwmbined with "u:" for user and tagtype for :normal or :critical
				var tagType = notificationRequest.IsCritical ? ":critical" : ":normal";
				var expression = string.Join(" || ", mTags.Select(a => "u:" + a.ToString()+ tagType).Skip(0).Take(20));

				try
				{
					var a = await _hub.SendTemplateNotificationAsync(templateParameters, expression);

					if (a != null)
					{
						var details = await _hub.GetNotificationOutcomeDetailsAsync(a.NotificationId);
						var startTime = DateTime.UtcNow;
						var timeout = TimeSpan.FromSeconds(5);

						//tiemout of 5 seconds to get the final state
						while (details.State == NotificationOutcomeState.Processing &&
							   DateTime.UtcNow - startTime < timeout)
						{
							await Task.Delay(500);
							details = await _hub.GetNotificationOutcomeDetailsAsync(a.NotificationId);
						}
					}
				}
				catch (Exception e)
				{
				}
			}
		}
		catch (Exception e)
		{
		}

		return true;
	}

	//Used by the migrator and the API to register users devices meant for V2
	public async Task<bool> RegisterDeviceAsync(RegisterTemplateRequest request, CancellationToken token)
	{
		try
		{
			var installation = await _hub.InstallationExistsAsync(request.UserId + "_" + request.Token.Substring(0, 8));
			if (!installation)
			{
				//clean up of the token for new flow
				var registrations = await _hub.GetRegistrationsByChannelAsync(request.Token.ToUpper(), 1000);
				if (registrations != null)
				{
					foreach (var reg in registrations)
					{
						await _hub.DeleteRegistrationAsync(reg);
					}
				}
			}

			var tags = new string[]
			{
				"c:" + request.ClientId,
				"u:" + request.UserId,
				"o:" + request.OS
			};

			var isIos = request.OS.ToLower() == "ios";

			var legacyTags = new string[]
			{
				request.UserId,
				isIos ? "iOS" : "Android"
			};

			// Define the installation
			var newInstallation = new Installation
			{
				InstallationId = request.UserId+"_"+request.Token.Substring(0,8),
				Platform = isIos ? NotificationPlatform.Apns : NotificationPlatform.Fcm,
				PushChannel = request.Token,
				Templates = new Dictionary<string, InstallationTemplate>
				{
                    // Template 1: Standard notification with alert
                    {
						"LegacyTemplate",
						new InstallationTemplate
						{
							Body = isIos ? LegacyApnTemplateBody : LegacyFcmTemplateBody,
							Tags = legacyTags
						}
					},
                    // Template 2: Critical Notification
                    {
						"CriticalTemplate",
						new InstallationTemplate
						{
							Body = isIos ? ApnCriticalTemplateBody : FcmCriticalTemplateBody,
							Tags = tags.Select(t => t + ":critical").ToList()
						}
					},
                    // Template 3: Normal Notifications
                    {
						"NormalTemplate",
						new InstallationTemplate
						{
							Body = isIos ? ApnTemplateBody : FcmTemplateBody,
							Tags = tags.Select(t => t + ":normal").ToList()
						}
					}
				}
			};

			await _hub.CreateOrUpdateInstallationAsync(newInstallation);

			return true;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error in RegisterDeviceAsync: {ex.Message}");
			return false;
		}
	}

	//Migrate all users of a client to new templates in V2
	public async Task MigrateClient(long clientID, CancellationToken token)
	{
		var users = new List<RegisterTemplateRequest>(); //Get from a query on database
		foreach (var user in users)
		{
			await RegisterDeviceAsync(user, token);
		}
	}
}