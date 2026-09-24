# Part 1 — AgentCore Gateway and Runtime

This walkthrough preserves the Bob's Used Books API as the business boundary, exports two read operations as MCP tools, connects a .NET 10 Microsoft Agent Framework agent, and packages it for AgentCore Runtime.

## 1. Build and deploy

Follow the ordered quickstart in the repository `README.md`. The consolidated CDK app creates `BobsBooksAuth`, `BobsBooksApi`, `AgentCoreGateway`, and `AgentRuntime`. The app tag is `sample=bobs-books-agentcore`. `STACK_POSTFIX` remains available for isolated copies.

## 2. Seed inventory

Run `scripts/seed-books.sh` (or `scripts/seed-books.ps1`). It writes the five evidence books directly to DynamoDB because the API's POST route requires a Cognito user.

## 3. Inspect tools before using a model

Set `AGENTCORE_GATEWAY_URL` from `cdk-outputs.json`, then run `src/BobsBooks.GatewayProbe`. It sends a SigV4-signed MCP `tools/list` request and expects only `ListBooks` and `GetBook`.

## 4. Run and test the agent

Set `AWS_REGION`, `AGENTCORE_GATEWAY_URL`, and `BEDROCK_MODEL_ID`. Run the agent integration test, then the console project. The test checks that a hardcover-mystery query uses `ListBooks` and returns *The Locked Room Ledger* and *Midnight at Ashcroft*. With no Gateway URL, the live test is reported as skipped.

## Least-privilege policies

Gateway's role is limited to:

```json
{
  "Effect": "Allow",
  "Action": "execute-api:Invoke",
  "Resource": [
    "arn:aws:execute-api:<region>:<account>:<api>/<stage>/GET/books",
    "arn:aws:execute-api:<region>:<account>:<api>/<stage>/GET/books/*"
  ]
}
```

The API resource policy permits `bedrock-agentcore.amazonaws.com` only when `aws:SourceAccount` is the deploying account and `aws:SourceArn` matches `arn:aws:bedrock-agentcore:<region>:<account>:gateway/bobs-books-gateway*`. Cognito-authorized POST, PUT, and cover-upload routes retain their existing resource-policy statement.

The Runtime role permits: pull from its CDK asset ECR repository; write to its own log group; invoke the `global.anthropic.claude-sonnet-4-6` inference profile and backing foundation model; invoke only the sample Gateway; emit required X-Ray spans; and publish metrics only under the `bedrock-agentcore` namespace. Its trust policy carries matching source-account and source-runtime-ARN conditions.

## Cleanup

Run `scripts/destroy.sh`. The default table, cover-page bucket, and Runtime log group are retained to prevent accidental data loss; remove them separately only after review.
