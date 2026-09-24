using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.Cognito;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.SSM;
using Amazon.CDK.AWS.VerifiedPermissions;
using BookInventoryApiStack.Api;
using Authorizer = BookInventoryApiStack.Api.Authorizer;
using CfnPolicy = Amazon.CDK.AWS.VerifiedPermissions.CfnPolicy;
using CfnPolicyProps = Amazon.CDK.AWS.VerifiedPermissions.CfnPolicyProps;
using Construct = Constructs.Construct;


namespace BookInventoryApiStack;

public sealed class BookInventoryServiceStack : Stack
{
    public string RestApiId { get; private set; } = null!;
    public string StageName { get; private set; } = null!;
    public string TableName { get; private set; } = null!;
    internal BookInventoryServiceStack(
        Construct scope,
        string id,
        BookInventoryServiceStackProps apiProps,
        IStackProps? props = null) : base(
        scope,
        id,
        props)
    {
        string servicePrefix = "BookInventoryService";

        // S3 bucket
        var bookInventoryBucket = new Bucket(this, $"{servicePrefix.ToLower()}-coverpage-images{apiProps.PostFix}",
            new BucketProps
            {
                BucketName =
                    $"{servicePrefix.ToLower()}-coverpage-images-{Stack.Of(this).Account}-{Stack.Of(this).Region}{apiProps.PostFix}",
                Versioned = true,
                RemovalPolicy = string.IsNullOrWhiteSpace(apiProps.PostFix)
                    ? RemovalPolicy.RETAIN
                    : RemovalPolicy.DESTROY // Destroy in postfix environment
            });

        //Database
        var bookInventory = new Table(this, $"{servicePrefix}-BookInventoryTable{apiProps.PostFix}", new TableProps
        {
            TableName = $"BookInventory{apiProps.PostFix}",
            PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute { Name = "BookId", Type = AttributeType.STRING },
            BillingMode = BillingMode.PAY_PER_REQUEST,
            RemovalPolicy = string.IsNullOrWhiteSpace(apiProps.PostFix)
                ? RemovalPolicy.RETAIN
                : RemovalPolicy.DESTROY // Destroy in postfix environment

        });
        bookInventory.AddGlobalSecondaryIndex(new GlobalSecondaryIndexProps()
        {
            IndexName = "GSI1",
            PartitionKey = new Amazon.CDK.AWS.DynamoDB.Attribute()
            {
                Name = "GSI1PK",
                Type = AttributeType.STRING
            },
            SortKey = new Amazon.CDK.AWS.DynamoDB.Attribute()
            {
                Name = "GSI1SK",
                Type = AttributeType.STRING
            },
        });

        // Retrieve user pool info from ssm
        var userPoolParameterValue =
            StringParameter.ValueForStringParameter(this, $"/bookstore/authentication/user-pool-id{apiProps.PostFix}");

        var userPool =
            UserPool.FromUserPoolArn(this, $"{servicePrefix}-UserPool{apiProps.PostFix}", userPoolParameterValue);

        _ = new CfnOutput(
            this,
            $"{servicePrefix}-User-Pool-Id{apiProps.PostFix}",
            new CfnOutputProps
            {
                Value = userPool.UserPoolId,
                ExportName = $"{servicePrefix}-UserPool{apiProps.PostFix}",
                Description = "UserPool"
            });
        var userPoolClientParameterValue =
            StringParameter.ValueForStringParameter(this,
                $"/bookstore/authentication/user-pool-client-id{apiProps.PostFix}");

        _ = new CfnOutput(
            this,
            $"{servicePrefix}-User-Pool-Client-Id{apiProps.PostFix}",
            new CfnOutputProps
            {
                Value = userPoolClientParameterValue,
                ExportName = $"{servicePrefix}-UserPool-Client{apiProps.PostFix}",
                Description = "UserPoolClientId"
            });

        var bookInventoryServiceStackProps = new BookInventoryServiceStackProps(apiProps.PostFix)
        {
            BucketName = bookInventoryBucket.BucketName,
            UserPoolId = userPool.UserPoolId,
            UserPoolClientId = userPoolClientParameterValue,
            Table = bookInventory.TableName
        };

        //Lambda Functions
        var getBookApi = new GetBookApi(
            this,
            $"GetBookEndpoint{apiProps.PostFix}",
            bookInventoryServiceStackProps);

        var addBooksApi = new AddBookApi(
            this,
            $"AddBooksEndpoint{apiProps.PostFix}",
            bookInventoryServiceStackProps);

        var listBooks = new ListBooksApi(
            this,
            $"ListBooksEndpoint{apiProps.PostFix}",
            bookInventoryServiceStackProps);

        var updateBooksApi = new UpdateBookApi(
            this,
            $"UpdateBooksEndpoint{apiProps.PostFix}",
            bookInventoryServiceStackProps);

        var getCoverPageUploadApi = new GetCoverPageUploadApi(
            this,
            $"GeneratePreSignedURLEndpoint{apiProps.PostFix}",
            bookInventoryServiceStackProps);

        // Configure Authorization Policies Amazon Verified Permissions (AVP)
        var apiAuthConfig = new List<ApiEndpointConfig>
        {
            new()
            {
                Path = "/books/{id}/{fileName}",
                Method = "GET",
                AllowedRoles = new List<string> { "Customer" }
            },
            new()
            {
                Path = "/books/{id}",
                Method = "PUT",
                AllowedRoles = new List<string> { "Customer", "Admin" }
            },
            new()
            {
                Path = "/books",
                Method = "POST",
                AllowedRoles = new List<string> { "Customer" }
            }
        };

        // Create AVP Policy Store
        var policyStore = new CfnPolicyStore(this, $"BookStorePolicyStore{apiProps.PostFix}", new CfnPolicyStoreProps
        {
            ValidationSettings = new CfnPolicyStore.ValidationSettingsProperty
            {
                Mode = "STRICT"
            },
            Schema = new CfnPolicyStore.SchemaDefinitionProperty
            {
                CedarJson = @"{
                       ""BookInventoryApi"": {
                        ""entityTypes"": {
                            ""User"": {
                                ""shape"": {
                                    ""type"": ""Record"",
                                    ""attributes"": {}
                                },
                                ""memberOfTypes"": [
                                    ""UserGroup""
                                ]
                            },
                            ""UserGroup"": {
                                ""shape"": {
                                    ""type"": ""Record"",
                                    ""attributes"": {}
                                }
                            },
                            ""Application"": {
                                ""shape"": {
                                    ""type"": ""Record"",
                                    ""attributes"": {}
                                }
                            }
                        },
                        ""actions"": {
                            ""get /books/{id}/{fileName}"": {
                                ""appliesTo"": {
                                    ""context"": {
                                        ""type"": ""Record"",
                                        ""attributes"": {}
                                    },
                                    ""principalTypes"": [
                                        ""User""
                                    ],
                                    ""resourceTypes"": [
                                        ""Application""
                                    ]
                                }
                            },
                            ""post /books"": {
                                ""appliesTo"": {
                                    ""context"": {
                                        ""type"": ""Record"",
                                        ""attributes"": {}
                                    },
                                    ""principalTypes"": [
                                           ""User""
                                    ],
                                    ""resourceTypes"": [
                                        ""Application""
                                    ]
                                }
                            },                            
                            ""put /books/{id}"": {
                                ""appliesTo"": {
                                    ""context"": {
                                        ""type"": ""Record"",
                                        ""attributes"": {}
                                    },
                                    ""principalTypes"": [
                                        ""User""
                                    ],
                                    ""resourceTypes"": [
                                        ""Application""
                                    ]
                                }
                            }                            
                        }
                    }
                }"
            },
            Description = "Policy store to define API authorization for Book store"
        });

        var identitySource = new CfnIdentitySource(this, $"CognitoIdentitySource{apiProps.PostFix}", new CfnIdentitySourceProps
        {
            PolicyStoreId = policyStore.Ref,
            PrincipalEntityType = "BookInventoryApi::User",
            Configuration = new CfnIdentitySource.IdentitySourceConfigurationProperty
            {
                CognitoUserPoolConfiguration = new CfnIdentitySource.CognitoUserPoolConfigurationProperty
                {
                    UserPoolArn = userPool.UserPoolArn,
                    ClientIds = [userPoolClientParameterValue],
                    GroupConfiguration = new CfnIdentitySource.CognitoGroupConfigurationProperty
                    {
                        GroupEntityType = "BookInventoryApi::UserGroup"
                    }
                }
            }
        });

        // First, group the endpoints by role
        var policiesByRole = apiAuthConfig
            .SelectMany(endpoint => endpoint.AllowedRoles.Select(role => new
            {
                Role = role,
                Action = $"{endpoint.Method.ToLower()} {endpoint.Path}"
            }))
            .GroupBy(x => x.Role)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Action).ToList());

        // Then create one policy per role
        var policyIndex = 0;
        foreach (var rolePolicy in policiesByRole)
        {
            var role = rolePolicy.Key;
            var actions = rolePolicy.Value;
            var userGroupId = $"{userPool.UserPoolId}|{role}";

            var actionsString = string.Join(", ", actions.Select(action =>
                $"BookInventoryApi::Action::\"{action}\""));

            var policyStatement = $@"permit(
                principal in BookInventoryApi::UserGroup::""{userGroupId}"",
                action in [{actionsString}],
                resource 
            );";

            new CfnPolicy(this, $"BookStorePolicy_{role}_{policyIndex++}{apiProps.PostFix}", new CfnPolicyProps
            {
                PolicyStoreId = policyStore.Ref,
                Definition = new CfnPolicy.PolicyDefinitionProperty
                {
                    Static = new CfnPolicy.StaticPolicyDefinitionProperty
                    {
                        Description = $"Policy defining permissions for {role} group",
                        Statement = policyStatement
                    }
                }
            });
        }

        // Setting Policy Store Id
        bookInventoryServiceStackProps.AvpPolicyStoreId = policyStore.Ref;

        // Authorizer for API Gateway
        var authorizer = new Authorizer(this, $"BookInventoryAuthorizer{apiProps.PostFix}",
            bookInventoryServiceStackProps);

        authorizer.Function.AddToRolePolicy(new PolicyStatement(new PolicyStatementProps
        {
            Effect = Effect.ALLOW,
            Actions = ["verifiedpermissions:IsAuthorizedWithToken"],
            Resources = [policyStore.AttrArn]
        }));

        // Harden the read routes without referencing the Gateway stack. The shared
        // deterministic name prefix avoids a cross-stack dependency cycle.
        var readRouteAuthorization = AuthorizationType.IAM;
        var gatewayArnPattern = FormatArn(new ArnComponents
        {
            Service = "bedrock-agentcore",
            Resource = "gateway",
            ResourceName = $"{BobsBooks.Infra.SampleConfiguration.GatewayNamePrefix}{apiProps.PostFix}*"
        });
        var apiResourcePolicy = new PolicyDocument(new PolicyDocumentProps
        {
            Statements =
            [
                new PolicyStatement(new PolicyStatementProps
                {
                    Sid = "AllowAgentCoreGatewayReadRoutes",
                    Effect = Effect.ALLOW,
                    Principals = [new ServicePrincipal("bedrock-agentcore.amazonaws.com")],
                    Actions = ["execute-api:Invoke"],
                    Resources = ["execute-api:/*/GET/books", "execute-api:/*/GET/books/*"],
                    Conditions = new Dictionary<string, object>
                    {
                        ["StringEquals"] = new Dictionary<string, object>
                        {
                            ["aws:SourceAccount"] = Account
                        },
                        ["ArnLike"] = new Dictionary<string, object>
                        {
                            ["aws:SourceArn"] = gatewayArnPattern
                        }
                    }
                }),
                new PolicyStatement(new PolicyStatementProps
                {
                    Sid = "AllowAuthorizerProtectedRoutes",
                    Effect = Effect.ALLOW,
                    Principals = [new AnyPrincipal()],
                    Actions = ["execute-api:Invoke"],
                    Resources =
                    [
                        "execute-api:/*/POST/books",
                        "execute-api:/*/PUT/books/*",
                        "execute-api:/*/GET/books/*/*"
                    ]
                })
            ]
        });

        var api = new SharedConstructs.Api(
                this,
                $"BookInventoryApi{apiProps.PostFix}",
                new RestApiProps
                {
                    RestApiName = $"BookInventoryApi{apiProps.PostFix}",
                    Policy = apiResourcePolicy,
                    // Remove the API Gateway CloudWatch role (and its account setting resource)
                    // with the stack; scripts/destroy.sh clears the dangling account setting.
                    CloudWatchRoleRemovalPolicy = RemovalPolicy.DESTROY,
                    DeployOptions = new StageOptions
                    {
                        AccessLogDestination =
                            new LogGroupLogDestination(new LogGroup(this, $"BookInventoryLogGroup{apiProps.PostFix}",
                                new LogGroupProps { RemovalPolicy = RemovalPolicy.DESTROY })),
                        AccessLogFormat = AccessLogFormat.JsonWithStandardFields(),
                        TracingEnabled = true,
                        LoggingLevel = MethodLoggingLevel.INFO
                    },
                    EndpointConfiguration = new EndpointConfiguration
                    {
                        Types = [EndpointType.REGIONAL]
                    }
                })
            .WithCognito(authorizer.Function)
            .WithEndpoint(
                "/books/{id}",
                HttpMethod.Get,
                getBookApi.Function,
                false,
                readRouteAuthorization) // Get Book by id: open upstream, SigV4 + Gateway-only when hardened
            .WithEndpoint(
                "/books",
                HttpMethod.Post,
                addBooksApi.Function) // Add Book, Customer
            .WithEndpoint(
                "/books",
                HttpMethod.Get,
                listBooks.Function,
                false,
                readRouteAuthorization,
                new Dictionary<string, bool>
                {
                    // Declared so the exported schema (and the Gateway ListBooks tool) exposes paging inputs.
                    { "method.request.querystring.pageSize", false },
                    { "method.request.querystring.cursor", false }
                }) // List books: open upstream, SigV4 + Gateway-only when hardened
            .WithEndpoint(
                "/books/{id}",
                HttpMethod.Put,
                updateBooksApi.Function) // Update Book, Admin
            .WithEndpoint(
                "/books/{id}/{fileName}",
                HttpMethod.Get,
                getCoverPageUploadApi.Function); // Cover page image, Customer

        RestApiId = api.RestApiId;
        StageName = api.DeploymentStage.StageName;
        TableName = bookInventory.TableName;

        //Grant DynamoDB Permission
        bookInventory.GrantReadData(getBookApi.Function.Role!);
        bookInventory.GrantReadData(listBooks.Function.Role!);
        bookInventory.GrantWriteData(addBooksApi.Function.Role!);
        bookInventory.GrantReadWriteData(updateBooksApi.Function.Role!);
        bookInventoryBucket.GrantPut(getCoverPageUploadApi.Function.Role!);

        _ = new CfnOutput(
            this,
            $"{servicePrefix}-APIEndpointOutput{apiProps.PostFix}",
            new CfnOutputProps
            {
                Value = api.Url,
                ExportName = $"{servicePrefix}-ApiEndpoint{apiProps.PostFix}",
                Description = "Endpoint of the Book Inventory API"
            });

        _ = new CfnOutput(
            this,
            $"{servicePrefix}-RestApiIdOutput{apiProps.PostFix}",
            new CfnOutputProps
            {
                Value = api.RestApiId,
                Description = "REST API ID, used as the AgentCore Gateway API Gateway target"
            });

        _ = new CfnOutput(
            this,
            $"{servicePrefix}-StageOutput{apiProps.PostFix}",
            new CfnOutputProps { Value = StageName, Description = "REST API stage" });

        _ = new CfnOutput(
            this,
            $"{servicePrefix}-TableNameOutput{apiProps.PostFix}",
            new CfnOutputProps { Value = TableName, Description = "DynamoDB seed table" });
    }
}