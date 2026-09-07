# CrowdStrike Domain Documentation Validation

Validate whether the Markdown files under `/Users/user/Dev/Uri/IntegrationsDomainDocs` can be relied on for understanding and using the CrowdStrike EDR client implementation under `/Users/user/Dev/AgentService/Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/EDR/CrowdStrike`.

Use three subagents:

- One reads the domain documentation and outputs a clear domain understanding.
- Two reads the CrowdStrike client implementation and outputs a clear client/mechanism understanding.
- Three mediates between both outputs and verifies whether the documentation corresponds to the implementation.

Publish the final report under `/Users/user/Dev/Uri/Planning`.
