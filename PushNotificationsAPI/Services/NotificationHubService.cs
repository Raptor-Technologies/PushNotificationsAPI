using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.NotificationHubs;
using Microsoft.Extensions.Options;
using PushNotificationsAPI.Models;
namespace PushNotificationsAPI.Services;

public class NotificationHubService : INotificationService
{
	public static string FcmTemplateBody { get; } = "{\"message\":{\"android\":{\"data\":{\"json\":\"$(jsonPayloadParam)\",\"title\":\"$(titleParam)\",\"body\":\"$(bodyParam)\",\"critical\":\"false\"}}}}";
	public static string FcmCriticalTemplateBody { get; } = "{\"message\":{\"android\":{\"data\":{\"json\":\"$(jsonPayloadParam)\",\"title\":\"$(titleParam)\",\"body\":\"$(bodyParam)\",\"critical\":\"true\",\"sound\":\"$(soundParam)\"},\"priority\":\"high\"}}}";

	public static string ApnNativeBody { get; } = "{\"aps\": {\"alert\" : { \"title\" : \"$(titleParam)\", \"body\" : \"$(bodyParam)\" }, \"json\":\"$(jsonPayloadParam)\"}}";
	public static string ApnNativeSoundCriticalBody { get; } = "{\"aps\": {\"alert\" : { \"title\" : \"$(titleParam)\", \"body\" : \"$(bodyParam)\" }, \"sound\" : {\"critical\" : 1, \"volume\" : 1.0, \"name\" : \"$(iosSoundParam)\"}, \"json\":\"$(jsonPayloadParam)\"}}";

	readonly NotificationHubClient _hub;

	public NotificationHubService(IOptions<NotificationHubOptions> options, IConfiguration configuration)
	{
		_hub = NotificationHubClient.CreateClientFromConnectionString(configuration["ConnectionStrings:AzureHubConnectionString"], configuration["ConnectionStrings:AzureHubName"]);
	}

	public async Task<object> GetRegistrationAsync(string tag)
	{
		var registrations = await _hub.GetRegistrationsByTagAsync(tag, 1000);
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

				//Select correct template based on critical or not
				var tagtype = notificationRequest.IsCritical ? ":critical" : ":normal";

				//create expression by batches of 20 tags (max allowed by AHN) combined with "u:" for user and tagtype for :normal or :critical
				var expression = string.Join(" || ", mTags.Select(a => "u:" + a.ToString() + tagtype).Skip(0).Take(20));

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
			//get ANH registration for an user
			var registration = await _hub.GetRegistrationsByTagAsync(request.UserId, 100);
			if (registration != null)
			{
				foreach (var reg in registration)
				{
					await _hub.DeleteRegistrationAsync(reg.RegistrationId);
				}
			}

			var tags = new string[]
			{
				"c:" + request.ClientId,
				"b:" + request.BuildingId,
				"u:" + request.UserId,
				"o:" + request.OS
			};

			//create new templates for the user
			if (request.OS == "ios")
			{
				await _hub.CreateAppleTemplateRegistrationAsync(request.Token, ApnNativeBody, tags.Select(a => a.ToString() + ":normal"));
				await _hub.CreateAppleTemplateRegistrationAsync(request.Token, ApnNativeSoundCriticalBody, tags.Select(a => a.ToString() + ":critical"));
			}
			else
			{
				await _hub.CreateFcmTemplateRegistrationAsync(request.Token, FcmTemplateBody, tags.Select(a => a.ToString() + ":normal"));
				await _hub.CreateFcmTemplateRegistrationAsync(request.Token, FcmCriticalTemplateBody, tags.Select(a => a.ToString() + ":critical"));
			}

			return true;
		}
		catch (Exception ex)
		{

			Console.WriteLine($"Error in RegisterOrUpdateiOSTokenAsync: {ex.Message}");
			return false;
		}
	}

	//Migrate all users of a client to new templates in V2
	public async Task MigrateClient(long clientID, CancellationToken token)
	{
		var users = new List<RegisterTemplateRequest>(); //Get from a query on database
		foreach(var user in users)
		{
			await RegisterDeviceAsync(user, token);
		}
	}
}