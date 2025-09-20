using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PushNotificationsAPI.Models;
using PushNotificationsAPI.Services;

namespace PushNotificationsAPI.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class NotificationsController : ControllerBase
{
	readonly INotificationService _notificationService;

	public NotificationsController(INotificationService notificationService)
	{
		_notificationService = notificationService;
	}

	[HttpPost]
	[Route("send")]
	[ProducesResponseType((int)HttpStatusCode.OK)]
	[ProducesResponseType((int)HttpStatusCode.BadRequest)]
	[ProducesResponseType((int)HttpStatusCode.UnprocessableEntity)]
	public async Task<IActionResult> RequestPush(
		[Required] NotificationRequest notificationRequest)
	{
		var success = await _notificationService.RequestNotificationAsync(notificationRequest, HttpContext.RequestAborted);

		if (!success)
			return new UnprocessableEntityResult();

		return new OkResult();
	}

	[HttpPost]
	[Route("register")]
	[ProducesResponseType((int)HttpStatusCode.OK)]
	[ProducesResponseType((int)HttpStatusCode.BadRequest)]
	[ProducesResponseType((int)HttpStatusCode.UnprocessableEntity)]
	public async Task<IActionResult> RegisteriOSToken([Required] RegisterTemplateRequest request)
	{
		var success = await _notificationService.RegisterDeviceAsync(request, HttpContext.RequestAborted);

		if (!success)
			return new UnprocessableEntityResult();

		return new OkResult();
	}

	[HttpGet]
	[Route("registraion")]
	[ProducesResponseType((int)HttpStatusCode.OK)]
	[ProducesResponseType((int)HttpStatusCode.BadRequest)]
	[ProducesResponseType((int)HttpStatusCode.UnprocessableEntity)]
	public async Task<IActionResult> GetRegistrationAsync(string tag)
	{
		var installation = await _notificationService.GetRegistrationAsync(tag);

		return Ok(installation);
	}
}

