﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using FindSimilarClient.Models;
using FindSimilarServices.Fingerprinting;
using SoundFingerprinting.DAO.Data;

namespace FindSimilarClient.Controllers
{
    public class HomeController : Controller
    {
        private IFindSimilarDatabase _database;

        public HomeController(IFindSimilarDatabase database)
        {
            _database = database;
        }

        public IActionResult Index(string query)
        {
            IList<TrackData> tracks = new List<TrackData>();
            if (!string.IsNullOrEmpty(query))
            {
                tracks = _database.ReadTracksByQuery(query);
            }
            ViewBag.Tracks = tracks;

            return View();
        }

        public IActionResult About()
        {
            ViewData["Message"] = "Your application description page.";

            return View();
        }

        public IActionResult Contact()
        {
            ViewData["Message"] = "Your contact page.";

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
