namespace Fuse.Application.Common.Behaviours;

public sealed class ValidationBehaviour<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
    {
        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(message, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count > 0)
        {
            throw new RequestValidationException(failures
                .GroupBy(f => f.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray()));
        }

        return await next(message, cancellationToken);
    }
}
