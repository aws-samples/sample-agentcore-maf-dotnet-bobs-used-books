using Amazon.VerifiedPermissions;
using Amazon.XRay.Recorder.Handlers.AwsSdk;
using BookInventory.Authorization.Utility;
using Microsoft.Extensions.DependencyInjection;

namespace BookInventory.Authorization;

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
        AWSSDKHandler.RegisterXRayForAllServices();
        services.AddAWSService<IAmazonVerifiedPermissions>(); // nosemgrep: missing-hsts-header
        services.AddScoped<ICognitoJwtVerifier, CognitoJwtVerifier>(); // nosemgrep: missing-hsts-header
    }
}