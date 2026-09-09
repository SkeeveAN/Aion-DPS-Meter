// The bucket every unrecognized boss name falls into - see src/matching/merge.ts.
export const UNASSIGNED_INSTANCE_NAME = "Unbekannt / nicht zugeordnet";

// The bucket every row created before server tracking existed is backfilled into (see the
// add-servers migration) - deliberately NOT a guess at which real server ("70.0.0.150:10241" etc.)
// those old rows actually came from. Two different config.ini readings turned up for the same
// physical install (bin64 vs bin32), so picking either one as the "real" historical fingerprint
// risked silently splitting that server's own future uploads into two buckets, or worse, colliding
// with a genuinely different server that happens to reuse an IP. This placeholder can never equal a
// real detected fingerprint (see Server/ServerIdentity.cs on the client: a real one is always
// "IP:PORT"), so it can only ever match itself.
export const UNKNOWN_SERVER_FINGERPRINT = "unattributed-pre-server-tracking";
