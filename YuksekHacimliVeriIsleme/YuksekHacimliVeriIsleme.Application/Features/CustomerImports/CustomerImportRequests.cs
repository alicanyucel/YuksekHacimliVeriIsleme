using MediatR;

namespace YuksekHacimliVeriIsleme.Application.Features.CustomerImports;

public sealed record UploadCustomerImportCommand(Stream Content, string FileName, long FileSize)
    : IRequest<CustomerImportJobDto>;

internal sealed class UploadCustomerImportCommandHandler : IRequestHandler<UploadCustomerImportCommand, CustomerImportJobDto>
{
    private readonly ICustomerImportService _service;

    public UploadCustomerImportCommandHandler(ICustomerImportService service)
    {
        _service = service;
    }

    public Task<CustomerImportJobDto> Handle(UploadCustomerImportCommand request, CancellationToken cancellationToken) =>
        _service.UploadAsync(request.Content, request.FileName, request.FileSize, cancellationToken);
}

public sealed record GetCustomerImportsQuery(int Page, int PageSize)
    : IRequest<CustomerImportPage<CustomerImportJobDto>>;

internal sealed class GetCustomerImportsQueryHandler : IRequestHandler<GetCustomerImportsQuery, CustomerImportPage<CustomerImportJobDto>>
{
    private readonly ICustomerImportRepository _repository;

    public GetCustomerImportsQueryHandler(ICustomerImportRepository repository)
    {
        _repository = repository;
    }

    public Task<CustomerImportPage<CustomerImportJobDto>> Handle(GetCustomerImportsQuery request, CancellationToken cancellationToken) =>
        _repository.GetJobsAsync(request.Page, request.PageSize, cancellationToken);
}

public sealed record GetCustomerImportQuery(Guid Id) : IRequest<CustomerImportJobDto?>;

internal sealed class GetCustomerImportQueryHandler : IRequestHandler<GetCustomerImportQuery, CustomerImportJobDto?>
{
    private readonly ICustomerImportRepository _repository;

    public GetCustomerImportQueryHandler(ICustomerImportRepository repository)
    {
        _repository = repository;
    }

    public Task<CustomerImportJobDto?> Handle(GetCustomerImportQuery request, CancellationToken cancellationToken) =>
        _repository.GetJobAsync(request.Id, cancellationToken);
}

public sealed record GetCustomerImportErrorsQuery(Guid Id, int Page, int PageSize)
    : IRequest<CustomerImportPage<CustomerImportErrorDto>?>;

internal sealed class GetCustomerImportErrorsQueryHandler : IRequestHandler<GetCustomerImportErrorsQuery, CustomerImportPage<CustomerImportErrorDto>?>
{
    private readonly ICustomerImportRepository _repository;

    public GetCustomerImportErrorsQueryHandler(ICustomerImportRepository repository)
    {
        _repository = repository;
    }

    public Task<CustomerImportPage<CustomerImportErrorDto>?> Handle(
        GetCustomerImportErrorsQuery request,
        CancellationToken cancellationToken) =>
        _repository.GetErrorsAsync(request.Id, request.Page, request.PageSize, cancellationToken);
}

public sealed record StreamCustomerImportErrorsQuery(Guid Id) : IStreamRequest<CustomerImportErrorDto>;

internal sealed class StreamCustomerImportErrorsQueryHandler
    : IStreamRequestHandler<StreamCustomerImportErrorsQuery, CustomerImportErrorDto>
{
    private readonly ICustomerImportRepository _repository;

    public StreamCustomerImportErrorsQueryHandler(ICustomerImportRepository repository)
    {
        _repository = repository;
    }

    public IAsyncEnumerable<CustomerImportErrorDto> Handle(
        StreamCustomerImportErrorsQuery request,
        CancellationToken cancellationToken) =>
        _repository.StreamErrorsAsync(request.Id, cancellationToken);
}
