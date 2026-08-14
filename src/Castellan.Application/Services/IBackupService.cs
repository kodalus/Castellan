using Castellan.Application.Dto;

namespace Castellan.Application.Services;

public interface IBackupService
{
    Task<CastellanExport> ExportAsync(CancellationToken ct = default);
    Task ImportAsync(CastellanExport data, CancellationToken ct = default);
}
