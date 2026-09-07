# AgentService Falcon: port correlated findings collection

Port the validated correlated collection strategy to the ancestor implementation at
/Users/user/Dev/AgentService/Source/CybiCollectors/FalconCollector (branch
feature/falcon-correlated-findings, already checked out). Reference (spec) implementations:
adapters repo Collectors/FalconCollector/Flows/Findings/ (production) and the prototype
FalconCorrelatedFindingsProbe.cs. Same strategy, this repo's idioms (Newtonsoft JObject,
ICybiBatchUploader/StreamWriter, CollectionStats, MakeApiCallWithTokenRefreshAsync).
