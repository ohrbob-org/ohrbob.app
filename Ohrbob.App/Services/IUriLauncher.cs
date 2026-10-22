using System;
using System.Threading.Tasks;

namespace Ohrbob.App.Services;

/// <summary>
/// Opens an external URI (typically in the user's browser). Abstracted so the
/// shared view model can be tested without an Avalonia <c>TopLevel</c>.
/// </summary>
public interface IUriLauncher
{
    Task LaunchAsync(Uri uri);
}
