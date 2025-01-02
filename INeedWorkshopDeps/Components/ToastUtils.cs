using System.Collections;
using UnityEngine;

namespace INeedWorkshopDeps.Components;

public class ToastUtils : MonoBehaviour
{
	private List<Toast> enqueuedToasts = new();
	
	public static ToastUtils Instance { get; private set; }

	private void Awake()
	{
		if (Instance != null)
		{
			Destroy(gameObject);
			return;
		}
		
		Debug.LogError("AAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
		               "AAAAAAAAAAAAAAAAAAAAAAAAAAAA");
		
		Instance = this;
	}
	
	public void EnqueueToast(string title, string body, ModalOption[] options)
	{
		enqueuedToasts.Add(new Toast(title, body, options));
	}

	private void Update()
	{
		if (enqueuedToasts.Count <= 0 || Modal.Instance.Open) return;
		
		var toast = enqueuedToasts.First();

		enqueuedToasts.RemoveAt(0);
		toast.Show();
	}
}

internal class Toast
{
	public string Title;
	public string Body;

	public ModalOption[] Options;

	public Toast(string title, string body, ModalOption[] options)
	{
		Title = title;
		Body = body;
		Options = options;
	}

	public void Show()
	{
		Modal.Show(Title, Body, Options);
	}
}