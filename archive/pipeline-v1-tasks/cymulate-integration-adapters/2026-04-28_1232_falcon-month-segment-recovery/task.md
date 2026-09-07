# Falcon Month Segment Recovery

Implement FalconCollector collection ordering so a host-provided base date is split into calendar month segments and collected from newest segment to oldest segment.

The collector must continue sending the existing next cursor for recovery and extend the recovery checkpoint with the currently collected month segment. Recovery must be able to resume from both values together.

