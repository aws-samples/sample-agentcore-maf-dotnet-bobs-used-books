using Amazon.S3;
using BookInventory.Api.Validators;
using BookInventory.Common;
using BookInventory.Models;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BookInventory.Api;

[Amazon.Lambda.Annotations.LambdaStartup]
public class Startup
{
    /// <summary>
    /// Services for Lambda functions can be registered in the services dependency injection container in this method.
    ///
    /// The services can be injected into the Lambda function through the containing type's constructor or as a
    /// parameter in the Lambda function using the FromService attribute. Services injected for the constructor have
    /// the lifetime of the Lambda compute container. Services injected as parameters are created within the scope
    /// of the function invocation.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        // Semgrep missing-hsts-header is suppressed below: these lines register Lambda services, not an ASP.NET Core web host.
        // The functions never serve HTTP; API Gateway fronts them and accepts only HTTPS, so HSTS does not apply.
        services.AddSharedServices(); // nosemgrep: missing-hsts-header
        services.AddDynamoDBServices(); // nosemgrep: missing-hsts-header
        services.AddAWSService<IAmazonS3>(); // nosemgrep: missing-hsts-header
        services.AddScoped<IValidator<CreateBookDto>, CreateBookDtoValidator>(); // nosemgrep: missing-hsts-header
        services.AddScoped<IValidator<UpdateBookDto>, UpdateBookDtoValidator>(); // nosemgrep: missing-hsts-header
    }
}