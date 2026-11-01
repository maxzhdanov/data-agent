# ISO 25012 operational rule seeds (FinGuard)

These notes are original operating guidance for the local audit agent. They are not verbatim standard text.

- Accuracy: parsed field values must match declared types and patterns before Kafka publish.
- Completeness: required fixed-width slices cannot be blank; dataset completeness should stay at or above 95%.
- Consistency: currency codes are ISO-like alphabetic triplets and amounts use invariant decimals.
- Traceability: every clean or DLQ record keeps schema id, line number, and correlation id.
- Recoverability: rejected rows are stored on the dead-letter topic with the original line.
- Confidentiality: account identifiers are treated as sensitive attributes in storage metadata.
- Portability: the same CleanRecord contract is persisted through MongoDB/Cosmos API or DynamoDB Local via the storage factory.
