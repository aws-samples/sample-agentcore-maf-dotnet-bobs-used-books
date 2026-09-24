# Bob's Used Books with .NET and Amazon Bedrock AgentCore

This repository is the companion sample for a five-part series that adds a Microsoft Agent Framework assistant to the existing Bob's Used Books serverless API. The sample preserves the API as the business boundary, exposes two read-only operations as Model Context Protocol (MCP) tools through Amazon Bedrock AgentCore Gateway, and hosts the .NET 10 agent on AgentCore Runtime.

## Architecture

A local console or application invokes the .NET agent. The agent uses an Amazon Bedrock model and discovers `ListBooks` and `GetBook` through an IAM-authorized AgentCore Gateway. Gateway assumes a least-privilege role to call only `GET /books` and `GET /books/{id}` on API Gateway. The API runs .NET 8 Lambda functions and stores inventory in DynamoDB. The Runtime exports signed OpenTelemetry traces to AWS X-Ray and CloudWatch.

## Series

| Post | Git tag | Docs page |
|---|---|---|
| Part 1 — Gateway and Runtime | `part-1` | [Part 1 walkthrough](docs/part-1.md) |
| Part 2 — Identity | `part-2` | Planned |
| Part 3 — Policy | `part-3` | Planned |
| Part 4 — Observability and evaluations | `part-4` | Planned |
| Part 5 — Runtime V2 and native AOT | `part-5` | Planned |

## Prerequisites

- .NET 10 SDK
- Docker with Linux Arm64 build support
- Node.js 20 or later
- AWS CDK v2
- AWS CLI v2 configured with temporary, least-privilege credentials
- An AWS account and Region where Amazon Bedrock AgentCore and the selected model are available

Do not attach `AdministratorAccess`. Use temporary credentials limited to creating and operating the resources in this sample, and review the synthesized IAM policies before deployment.

## Quickstart

Run these commands in order from the repository root:

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROLL_FORWARD=Major
export AWS_REGION=us-east-1
export CDK_DEFAULT_REGION="$AWS_REGION"
export CDK_DEFAULT_ACCOUNT="$(aws sts get-caller-identity --query Account --output text)"

dotnet build BobsBooks.sln
npx -y aws-cdk@2 bootstrap
npx -y aws-cdk@2 deploy --all --require-approval never --outputs-file cdk-outputs.json
./scripts/seed-books.sh

export AGENTCORE_GATEWAY_URL="$(node -e 'const o=require("./cdk-outputs.json"); const s=Object.values(o).find(x=>x.GatewayUrl); process.stdout.write(s.GatewayUrl)')"
export BEDROCK_MODEL_ID=global.anthropic.claude-sonnet-4-6

dotnet run --project src/BobsBooks.GatewayProbe/BobsBooks.GatewayProbe.csproj
dotnet test tests/BobsBooks.Agent.Tests/BobsBooks.Agent.Tests.csproj
dotnet run --project src/BobsBooks.Console/BobsBooks.Console.csproj
```

The Gateway probe performs a SigV4-signed MCP `tools/list` request without invoking a model. The agent test uses the real model, Gateway, and API; it skips cleanly when `AGENTCORE_GATEWAY_URL` is absent.

## Cleanup

```bash
./scripts/destroy.sh
```

The DynamoDB table, cover-page bucket, and Runtime log group use `RETAIN` in the default environment. Remove retained data manually only after confirming that it is no longer needed. Do not delete a shared CDK bootstrap stack.

## Cost

This sample uses pay-per-use services, including Amazon Bedrock model inference, AgentCore Runtime and Gateway, Lambda, API Gateway, DynamoDB, and observability ingestion. Charges accrue only for usage and retained storage, but retained resources can continue to incur storage charges after stack deletion.

## Security

The two read routes require AWS IAM authorization. Gateway can invoke only those GET routes, the API resource policy restricts calls to the AgentCore service from the deploying account and this sample's Gateway name prefix, and the Runtime role is scoped to its image, log group, model, Gateway, traces, and AgentCore metric namespace. Keep prompt and tool-result telemetry disabled unless your data-handling review permits it.

## Upstream

Forked from [aws-samples/bobs-used-bookstore-serverless](https://github.com/aws-samples/bobs-used-bookstore-serverless) at commit `60705cc29dd8c4721a33348d047331fc8f38fb9a` under MIT-0. See `NOTICE` for the modification list. The upstream `LICENSE`, `CODE_OF_CONDUCT.md`, and `CONTRIBUTING.md` are retained.
