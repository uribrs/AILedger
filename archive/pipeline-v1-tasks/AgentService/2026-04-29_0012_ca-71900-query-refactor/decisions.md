* Use a full task contract because the work spans multiple integrations, tests, and external ticket context.
* Proceed with implementation because the user supplied the Jira screenshot showing the parent title and relevant child items.
* Treat the child items CA-71934 ElasticSiem, CA-71936 InsightIDR, and CA-71938 NetWitness as the exact implementation scope.
* Prefer converting only standard/custom cache paths; advanced custom query behavior remains out of scope unless CA-71900 explicitly says otherwise.
* Keep existing vendor endpoints, auth, pagination, and response parsing intact; only remove IOC/keyword filtering from standard query construction.
* Use explicit result types when a time-range group can represent multiple result flags and a vendor writer emits Alert and Event cache details separately.
* For InsightIDR, omit the `query` parameter on standard time-range logset searches instead of passing an empty query value; advanced custom queries still include the query parameter.
