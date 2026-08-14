using System.Text.Json;
using Castellan.Application.Services;

namespace Castellan.Application.UseCases;

public sealed class ExportDataUseCase(IBackupService backup)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public async Task<string> ExecuteAsync(CancellationToken ct = default)
    {
        var data = await backup.ExportAsync(ct);
        return JsonSerializer.Serialize(data, JsonOpts);
    }
}
