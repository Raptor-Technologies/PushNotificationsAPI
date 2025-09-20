namespace PushNotificationsAPI.Models
{
	public class RegisterTemplateRequest
	{
		public string UserId { get; set; }
		public string BuildingId { get; set; }
		public string ClientId { get; set; }
		public string OS { get; set; }
		public string Token { get; set; }
	}
}
