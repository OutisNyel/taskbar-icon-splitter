import { DomainWindowOrganizer } from "./organizer";

const organizer = new DomainWindowOrganizer();
organizer.registerListeners();
void organizer.initialize();

chrome.runtime.onInstalled.addListener((details) => {
  if (details.reason !== "install") {
    return;
  }
  void chrome.tabs.create({
    url: chrome.runtime.getURL("welcome.html")
  });
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  const handle = async (): Promise<unknown> => {
    switch (message?.type) {
      case "get_status":
        return organizer.getStatus();
      case "set_enabled":
        return organizer.setEnabled(Boolean(message.enabled));
      case "organize_now":
        return organizer.organizeAll();
      default:
        throw new Error(`Unknown message type: ${String(message?.type)}`);
    }
  };

  void handle().then(
    (response) => sendResponse(response),
    (error) =>
      sendResponse({
        error: error instanceof Error ? error.message : String(error)
      })
  );
  return true;
});
