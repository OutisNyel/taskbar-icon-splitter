const token = new URL(location.href).searchParams.get("token");

if (token) {
  document.title = `TIS:${token}`;
}
