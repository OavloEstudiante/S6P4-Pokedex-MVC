using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Mvc;
using Newtonsoft.Json;
using S6P4.Models;

namespace S6P4.Controllers
{
    public class PokemonController : Controller
    {
        private const int PorPagina = 20;

        // HttpClient único y reutilizable (buena práctica: no crear uno por petición)
        private static readonly HttpClient _client = CrearCliente();

        private static HttpClient CrearCliente()
        {
            // PokéAPI exige TLS 1.2 (importante en .NET Framework 4.x)
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var cliente = new HttpClient
            {
                BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
                Timeout = TimeSpan.FromSeconds(30)
            };

            cliente.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            cliente.DefaultRequestHeaders.UserAgent.ParseAdd("S6P4/1.0");

            // PokéAPI es pública y NO requiere token.
            // Si usaras otra API con autenticación, aquí agregarías el encabezado:
            // cliente.DefaultRequestHeaders.Authorization =
            //     new AuthenticationHeaderValue("Bearer", "TU_TOKEN_AQUI");

            return cliente;
        }

        // GET: /Pokemon?pagina=1
        public async Task<ActionResult> Index(int pagina = 1)
        {
            if (pagina < 1) pagina = 1;
            var vm = new PokemonListaViewModel { Pagina = pagina };

            try
            {
                int offset = (pagina - 1) * PorPagina;

                // 1) Lista paginada: GET pokemon?limit=20&offset=N
                string json = await _client.GetStringAsync("pokemon?limit=" + PorPagina + "&offset=" + offset);
                var lista = JsonConvert.DeserializeObject<PokemonListResponse>(json);

                vm.TotalPokemon = lista.Count;
                vm.TotalPaginas = (int)Math.Ceiling(lista.Count / (double)PorPagina);

                // 2) Detalle de cada Pokémon en paralelo (para obtener imagen y tipos)
                var tareas = lista.Results.Select(r => ObtenerDetalleAsync(r.Name));
                var detalles = await Task.WhenAll(tareas);
                vm.Pokemones = detalles.Where(d => d != null).OrderBy(d => d.Id).ToList();
            }
            catch (HttpRequestException ex)
            {
                vm.Error = "No se pudo conectar con PokéAPI: " + ex.Message;
            }
            catch (TaskCanceledException)
            {
                vm.Error = "La solicitud tardó demasiado (timeout). Intenta de nuevo.";
            }
            catch (JsonException ex)
            {
                vm.Error = "Error al interpretar el JSON recibido: " + ex.Message;
            }

            return View(vm);
        }

        // GET: /Pokemon/Buscar?q=pikachu
        public ActionResult Buscar(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return RedirectToAction("Index");

            return RedirectToAction("Detalle", new { id = q.Trim().ToLowerInvariant() });
        }

        // GET: /Pokemon/Detalle/25   o   /Pokemon/Detalle/pikachu
        public async Task<ActionResult> Detalle(string id)
        {
            var vm = new PokemonDetalleViewModel();

            try
            {
                vm.Pokemon = await ObtenerDetalleAsync(id);

                if (vm.Pokemon == null)
                {
                    vm.Error = "No se encontró ningún Pokémon con el nombre o número \"" + id + "\".";
                    return View(vm);
                }

                vm.Descripcion = await ObtenerDescripcionAsync(vm.Pokemon.Id);
            }
            catch (Exception ex)
            {
                vm.Error = "Ocurrió un error al consultar la API: " + ex.Message;
            }

            return View(vm);
        }

        // -------- Métodos privados --------

        private static async Task<PokemonDetail> ObtenerDetalleAsync(string idONombre)
        {
            var resp = await _client.GetAsync("pokemon/" + Uri.EscapeDataString(idONombre));

            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();

            string json = await resp.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<PokemonDetail>(json);
        }

        private static async Task<string> ObtenerDescripcionAsync(int id)
        {
            try
            {
                string json = await _client.GetStringAsync("pokemon-species/" + id);
                var especie = JsonConvert.DeserializeObject<PokemonSpecies>(json);

                var entrada = especie.FlavorTextEntries.FirstOrDefault(e => e.Language.Name == "es")
                           ?? especie.FlavorTextEntries.FirstOrDefault(e => e.Language.Name == "en");

                return entrada == null ? null
                    : entrada.FlavorText.Replace("\n", " ").Replace("\f", " ").Replace("­", "");
            }
            catch
            {
                return null; // la descripción es opcional
            }
        }
    }
}