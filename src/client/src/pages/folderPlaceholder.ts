/**
 * The example a folder path field shows, in the shape of the platform the browser runs on: a drive
 * letter on Windows, a home folder on macOS and on Linux. A new user on a Mac was shown
 * "C:\path\to\project" (etalii.adp spec 092, host test T035).
 */
export function folderPlaceholder(platform: string): string {
  if (/win/i.test(platform) && !/darwin/i.test(platform)) return "C:\\path\\to\\project";
  if (/mac|darwin|iphone|ipad/i.test(platform)) return "/Users/you/project";
  return "/home/you/project";
}

/** The platform the browser reports, newest property first. */
export function browserPlatform(): string {
  const withData = navigator as Navigator & { userAgentData?: { platform?: string } };
  return withData.userAgentData?.platform || navigator.platform || navigator.userAgent;
}
