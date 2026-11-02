using Microsoft.AspNetCore.Mvc;

namespace DistributedRateLimiter.Api.Controllers;

public record ProductDto(int Id, string Name, decimal Price, string Category);

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private static readonly List<ProductDto> SampleProducts = new()
    {
        new ProductDto(1, "Cloud Server Small", 15.00m, "Compute"),
        new ProductDto(2, "Cloud Server Large", 60.00m, "Compute"),
        new ProductDto(3, "Managed Redis Cluster", 45.00m, "Database"),
        new ProductDto(4, "PostgreSQL High-Availability", 85.00m, "Database"),
        new ProductDto(5, "Global Edge CDN", 25.00m, "Network")
    };

    /// <summary>
    /// Retrieves a list of available cloud products.
    /// Rate limited based on client identity and tier.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult GetProducts()
    {
        return Ok(SampleProducts);
    }

    /// <summary>
    /// Retrieves a single product by identifier.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult GetProductById(int id)
    {
        var product = SampleProducts.FirstOrDefault(p => p.Id == id);
        if (product == null)
        {
            return NotFound(new { message = $"Product with ID {id} not found." });
        }

        return Ok(product);
    }

    /// <summary>
    /// High-impact mutation endpoint subject to strict rate limits.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult CreateProduct([FromBody] ProductDto newProduct)
    {
        if (string.IsNullOrWhiteSpace(newProduct.Name) || newProduct.Price <= 0)
        {
            return BadRequest(new { message = "Invalid product name or price." });
        }

        var created = newProduct with { Id = SampleProducts.Count + 1 };
        return CreatedAtAction(nameof(GetProductById), new { id = created.Id }, created);
    }
}
