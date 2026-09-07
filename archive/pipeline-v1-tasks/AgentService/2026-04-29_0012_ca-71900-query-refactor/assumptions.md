* VALIDATED: CA-71900 is titled "Query Integrations - Convert to Time Range" and includes child items CA-71934 ElasticSiem, CA-71936 InsightIDR, and CA-71938 NetWitness.
* VALIDATED: No existing local task state for CA-71900 was found under `ai/active/`.
* VALIDATED: The current session has no exposed Atlassian Rovo/Jira callable tool, and generic web access did not provide the private Jira issue content.
* REJECTED: Jira ticket details may add, remove, or prioritize integrations differently than the current user message; user screenshot shows the relevant child items for this work.
* VALIDATED: ElasticSiem standard/custom query cache generation currently uses `generateCacheByIoc` for both Event and Alert.
* VALIDATED: InsightIDR standard/custom query cache generation currently uses `generateCacheByIoc` for Event.
* VALIDATED: NetWitness standard/custom query cache generation currently uses `generateCacheByTimeRange` for Alert and `generateCacheByIoc` for Event.
* VALIDATED: For the requested refactor, standard query request details should not carry the IOC/keyword query string after conversion; downstream cache matching remains responsible for keyword evaluation.
* VALIDATED: The broader Executor test project cannot currently execute the ElasticSiem helper test in this environment because unrelated dependency/build issues occur before test discovery completes.
* VALIDATED: InsightIDR standard time-range event search can be represented without sending a dummy or empty keyword query parameter in code; tests pass with query omission.
