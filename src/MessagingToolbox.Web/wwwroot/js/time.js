// Converts a datetime-local value (the browser's wall-clock time) to a UTC ISO string using the
// browser's own time-zone rules for that date, DST included. The server's zone is irrelevant here:
// in a container it is UTC, not the user's. Returns null for an unparsable value.
export function localToUtcIso(localValue) {
    const date = new Date(localValue);
    return isNaN(date.getTime()) ? null : date.toISOString();
}
