using PushNotificationsAPI.Models;

namespace PushNotificationsAPI.Services;

public interface INotificationService
{
		Task<object> GetRegistrationAsync(string tag);
		Task<bool> RequestNotificationAsync(NotificationRequest notificationRequest, CancellationToken token);
		Task<bool> RegisterDeviceAsync(RegisterTemplateRequest request, CancellationToken token);
}

