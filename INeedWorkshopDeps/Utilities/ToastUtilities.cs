using UnityEngine;
using UnityEngine.UI;

namespace INeedWorkshopDeps;

public class ToastUtilities : MonoBehaviour {
	private struct Toast(string title, string body, ModalOption[] options) {
		public readonly string Title = title;
		public readonly string Body = body;
		public readonly ModalOption[] Options = options;
	}
	
	private static readonly List<Toast> EnqueuedToasts = [];
	private static bool Initialized = false;
	
	/// <summary>
	/// Enqueues a toast to be shown.
	/// If the modal is already open, the modal will be shown after the current modal is closed.
	/// Initializes the ToastUtilities if it has not been initialized yet.
	/// </summary>
	/// <param name="title"> The title of the toast. </param>
	/// <param name="body"> The body of the toast. </param>
	/// <param name="options"> The options for the toast. </param>
	public static void EnqueueToast(string title, string body, ModalOption[] options) { 
		if (!Initialized) { Modal.Instance.gameObject.AddComponent<ToastUtilities>(); Initialized = true; }
		EnqueuedToasts.Add(new Toast(title, body, options)); 
	}

	private void LateUpdate() {
		if (EnqueuedToasts.Count == 0 || Modal.Instance.Open) { return; }
		Toast toast = EnqueuedToasts[0];
		EnqueuedToasts.RemoveAt(0);
		ShowToast(toast);
	}

	private static void ShowToast(Toast toast) {
		Modal.Show(toast.Title, toast.Body, toast.Options);
	}
}