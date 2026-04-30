using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace YT.Generate.Cuts.Application.Abstractions.Common;
public class ValidationBehavior
{
    private readonly IServiceProvider _serviceProvider;

    public ValidationBehavior(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task ValidateAsync<TRequest>(TRequest request, CancellationToken ct)
    {
        var validators = _serviceProvider.GetServices<IValidator<TRequest>>();

        if (!validators.Any())
            return;

        var context = new ValidationContext<TRequest>(request);

        var validationResults = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, ct)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);
    }
}
