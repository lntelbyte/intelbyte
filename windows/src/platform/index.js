import {
  setupApps,
  unwireShortcuts,
  runApp,
  runAppArgv,
  inspectAppState,
  runningUnprotected,
  killApp,
  relaunchApp,
  relaunchUnprotectedApps,
} from './windows/apps.js';
import { scrubChromium, isBrowserRunning, installedChromiumBrowsers } from './windows/chromium.js';
import * as firefox from './windows/firefox.js';

if (process.platform !== 'win32') {
  console.error(
    `intelbyte (Windows edition) can't run on "${process.platform}". ` +
      'Use the Linux edition on Linux.'
  );
  process.exit(1);
}

export default {
  setupApps,
  unwire(registry) {
    return { overrides: unwireShortcuts(registry), shims: [] };
  },
  runApp,
  runAppArgv,
  inspectAppState,
  runningUnprotected,
  killApp,
  relaunchApp,
  relaunchUnprotectedApps,
  installedChromiumBrowsers,
  isBrowserRunning,
  scrubChromium,
  firefox,
};
