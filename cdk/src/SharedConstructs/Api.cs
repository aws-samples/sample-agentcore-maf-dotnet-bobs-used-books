namespace SharedConstructs;

using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.Lambda;

using Constructs;

using HttpMethod = HttpMethod;

public class Api : RestApi
{
    public RequestAuthorizer Authorizer { get; private set; }

    public Api(
        Construct scope,
        string id,
        RestApiProps props) : base(
        scope,
        id,
        props)
    {
    }

    public Api WithCognito(Function authorizerFunction)
    {
        this.Authorizer = new RequestAuthorizer(this, "CognitoTokenAuthorizer", new RequestAuthorizerProps()
        {
            AuthorizerName = "cognitotokenauthorizer",
            IdentitySources = ["method.request.header.Authorization", "context.httpMethod", "context.path"],
            Handler = authorizerFunction
        });
        return this;
    }

    /// <param name="authorizationTypeOverride">
    /// Authorization for a method that does not use the Cognito authorizer (<paramref name="authorizeApi"/> = false).
    /// Defaults to <see cref="AuthorizationType.NONE"/>; pass <see cref="AuthorizationType.IAM"/> to require SigV4.
    /// </param>
    /// <param name="requestParameters">
    /// Method request parameters to declare, e.g. <c>method.request.querystring.pageSize</c> = false (optional).
    /// Declared parameters appear in the exported OpenAPI schema, which AgentCore Gateway turns into tool inputs.
    /// </param>
    public Api WithEndpoint(
        string path,
        HttpMethod method,
        Function function,
        bool authorizeApi = true,
        AuthorizationType? authorizationTypeOverride = null,
        IDictionary<string, bool>? requestParameters = null)
    {
        IResource? lastResource = null;

        foreach (var pathSegment in path.Split('/'))
        {
            var sanitisedPathSegment = pathSegment.Replace(
                "/",
                "");

            if (string.IsNullOrEmpty(sanitisedPathSegment))
            {
                continue;
            }

            if (lastResource == null)
            {
                lastResource = this.Root.GetResource(sanitisedPathSegment) ?? this.Root.AddResource(sanitisedPathSegment);
                continue;
            }

            lastResource = lastResource.GetResource(sanitisedPathSegment) ??
                           lastResource.AddResource(sanitisedPathSegment);
        }

        lastResource?.AddMethod(
            method.ToString().ToUpper(),
            new LambdaIntegration(function),
            new MethodOptions
            {
                MethodResponses = new IMethodResponse[]
                {
                    new MethodResponse { StatusCode = "200" },
                    new MethodResponse { StatusCode = "400" },
                    new MethodResponse { StatusCode = "500" }
                },
                AuthorizationType = authorizeApi
                    ? AuthorizationType.CUSTOM
                    : authorizationTypeOverride ?? AuthorizationType.NONE,
                Authorizer = authorizeApi ? this.Authorizer : null,
                RequestParameters = requestParameters
            });

        return this;
    }
}