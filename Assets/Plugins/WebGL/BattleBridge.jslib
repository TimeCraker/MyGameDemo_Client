mergeInto(LibraryManager.library, {
  NotifyReactBattleResult: function (resultTypePtr, payloadPtr) {
    var resultType = UTF8ToString(resultTypePtr);
    var payload = UTF8ToString(payloadPtr);
    window.dispatchEvent(
      new CustomEvent("unity:battle_result", {
        detail: { resultType: resultType, payload: payload },
      })
    );
  },
});

