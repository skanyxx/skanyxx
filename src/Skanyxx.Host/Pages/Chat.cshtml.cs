using Microsoft.AspNetCore.Mvc.RazorPages;
using Skanyxx.Core.Platform;

namespace Skanyxx.Host.Pages
{
    public class ChatModel : PageModel
    {
        /// <summary>
        /// The signed-in person: Chat creates each kagent session as them, so a reopened conversation is read as them
        /// too (an owner's default read is the console's own user, D124, which does not have it).
        /// </summary>
        public string? Me => Caller.UserId(User);

        public void OnGet()
        {
        }
    }
}
