export type PublishedComparisonReference = {
  programId: string;
  earlierVersion: number;
  currentVersion: number;
} | 'invalid' | null;

/** A URL requests a comparison; publication must be confirmed by exact source reads. */
export function requestedPublishedComparison(search: string): PublishedComparisonReference {
  const params = new URLSearchParams(search);
  const hasComparisonParameter = ['compareProgramId', 'earlierVersion', 'currentVersion']
    .some(key => params.has(key));
  if (!hasComparisonParameter) return null;
  const ids = params.getAll('compareProgramId');
  const earlierValues = params.getAll('earlierVersion');
  const currentValues = params.getAll('currentVersion');
  if (ids.length !== 1 || earlierValues.length !== 1 || currentValues.length !== 1 ||
      params.size !== 3) return 'invalid';

  const programId = ids[0];
  const earlierRaw = earlierValues[0];
  const currentRaw = currentValues[0];
  const earlierVersion = Number(earlierRaw);
  const currentVersion = Number(currentRaw);
  const validId = programId.length > 0 && programId.trim() === programId &&
    programId.length <= 256 && ![...programId].some(c => /[\u0000-\u001f\u007f]/.test(c));
  const validVersion = (raw: string, value: number) =>
    /^[1-9][0-9]*$/.test(raw) && Number.isSafeInteger(value) &&
    value <= 2147483647 && String(value) === raw;
  if (!validId || !validVersion(earlierRaw, earlierVersion) ||
      !validVersion(currentRaw, currentVersion) || earlierVersion >= currentVersion)
    return 'invalid';
  return { programId, earlierVersion, currentVersion };
}

export function publishedComparisonHref(programId: string, earlierVersion: number,
  currentVersion: number, origin = window.location.origin,
  pathname = window.location.pathname): string {
  const url = new URL(pathname, origin);
  url.search = new URLSearchParams({
    compareProgramId: programId,
    earlierVersion: String(earlierVersion),
    currentVersion: String(currentVersion),
  }).toString();
  url.hash = '';
  return url.pathname + url.search;
}
