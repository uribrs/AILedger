# Falcon Partial Page Success

Implement intermediate exit behavior for FalconCollector so a non-recoverable failure after at least one page has been collected is reported as DONE with the failure attached as context.

The behavior applies only when page progress is greater than zero. Failures before any page remains collected must continue to follow the normal failed status path.
