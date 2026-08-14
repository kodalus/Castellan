using System.Text.Json;
using Castellan.Application.Dto;
using Castellan.Application.Services;

namespace Castellan.Application.UseCases;

public sealed class ImportDataUseCase(IBackupService backup)
{
    public async Task ExecuteAsync(string json, CancellationToken ct = default)
    {
        var data = JsonSerializer.Deserialize<CastellanExport>(json)
            ?? throw new InvalidOperationException("Nieprawidłowy format pliku kopii zapasowej.");

        if (data.Version != 1)
            throw new InvalidOperationException($"Nieobsługiwana wersja kopii zapasowej: {data.Version}.");

        await backup.ImportAsync(data, ct);
    }
}
