using System.Text;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using YuksekHacimliVeriIsleme.Application.Features.CustomerImports;
using YuksekHacimliVeriIsleme.WebAPI.Abstractions;

namespace YuksekHacimliVeriIsleme.WebAPI.Controllers;

public sealed class CustomerImportsController : ApiController
{
    public CustomerImportsController(IMediator mediator) : base(mediator)
    {
    }

    [HttpPost("/api/customer-imports")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CustomerImportJobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest(new { message = "CSV dosyası zorunludur (file alanı)." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var command = new UploadCustomerImportCommand(stream, file.FileName, file.Length);
            var job = await _mediator.Send(command, cancellationToken);
            return AcceptedAtAction(nameof(GetJob), new { id = job.Id }, job);
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpGet("/api/customer-imports")]
    [ProducesResponseType(typeof(CustomerImportPage<CustomerImportJobDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerImportPage<CustomerImportJobDto>>> GetJobs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetCustomerImportsQuery(page, pageSize), cancellationToken);
        return Ok(result);
    }

    [HttpGet("/api/customer-imports/{id:guid}")]
    [ProducesResponseType(typeof(CustomerImportJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerImportJobDto>> GetJob(Guid id, CancellationToken cancellationToken)
    {
        var job = await _mediator.Send(new GetCustomerImportQuery(id), cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpGet("/api/customer-imports/{id:guid}/errors")]
    [ProducesResponseType(typeof(CustomerImportPage<CustomerImportErrorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerImportPage<CustomerImportErrorDto>>> GetErrors(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var errors = await _mediator.Send(new GetCustomerImportErrorsQuery(id, page, pageSize), cancellationToken);
        return errors is null ? NotFound() : Ok(errors);
    }

    [HttpGet("/api/customer-imports/{id:guid}/errors/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportErrors(Guid id, CancellationToken cancellationToken)
    {
        if (await _mediator.Send(new GetCustomerImportQuery(id), cancellationToken) is null)
        {
            return NotFound();
        }

        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.Append("Content-Disposition", $"attachment; filename=import-errors-{id:N}.csv");
        await using var writer = new StreamWriter(Response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 1024, leaveOpen: true);
        await writer.WriteLineAsync("Row Number,Raw Data,Error Reason");

        await foreach (var error in _mediator.CreateStream(new StreamCustomerImportErrorsQuery(id), cancellationToken))
        {
            await writer.WriteLineAsync($"{error.RowNumber},{ToCsvCell(error.RawData)},{ToCsvCell(error.ErrorReason)}");
        }

        await writer.FlushAsync(cancellationToken);
        return new EmptyResult();
    }

    private static string ToCsvCell(string value)
    {
        var firstNonWhitespace = value.FirstOrDefault(character => !char.IsWhiteSpace(character));
        if (firstNonWhitespace is '=' or '+' or '-' or '@')
        {
            value = $"'{value}";
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
