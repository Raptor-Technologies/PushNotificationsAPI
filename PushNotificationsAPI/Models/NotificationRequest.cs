namespace PushNotificationsAPI.Models;

public class NotificationRequest
{
	public Guid[] Tags { get; set; } = Array.Empty<Guid>();

	public string Json { get; set; }
	public string Body { get; set; }
	public string Title { get; set; }
	public string Sound { get; set; }
	public bool IsCritical { get; set; }
}